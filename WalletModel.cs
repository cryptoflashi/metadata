using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using NBitcoin;
using ReactiveUI;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Infrastructure;
using WalletWasabi.Fluent.Models.Transactions;
using WalletWasabi.Fluent.ViewModels.Wallets.Labels;
using WalletWasabi.WabiSabi.Client;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.Models.Wallets;

public partial interface IWalletModel : INotifyPropertyChanged;

[AppLifetime]
[AutoInterface]
public partial class WalletModel : ReactiveObject
{
	private readonly Lazy<IWalletCoinjoinModel?> _coinjoin;
	private readonly Lazy<IWalletCoinsModel> _coins;

	[AutoNotify] private bool _isLoggedIn;
	[AutoNotify] private bool _isLoaded;
	[AutoNotify] private bool _isSelected;

	public WalletModel(Wallet wallet, IAmountProvider amountProvider)
	{
		Wallet = wallet;
		AmountProvider = amountProvider;

		Auth = new WalletAuthModel(this, Wallet);
		Loader = new WalletLoadWorkflow(Wallet);
		Settings = new WalletSettingsModel(Wallet.KeyManager);

		_coinjoin = new(() =>
		{
			var coinJoinManager = Services.HostedServices.GetOrDefault<CoinJoinManager>();
			return coinJoinManager is not null
				? new WalletCoinjoinModel(Wallet, coinJoinManager, Settings)
				: null;
		});

		_coins = new(() => new WalletCoinsModel(wallet, this));

		Transactions = new WalletTransactionsModel(this, wallet);

		Addresses = new AddressesModel(Wallet);

		State =
			Observable.FromEventPattern<WalletState>(Wallet, nameof(Wallet.StateChanged))
					  .ObserveOn(RxApp.MainThreadScheduler)
					  .Select(_ => Wallet.State);

		Privacy = new WalletPrivacyModel(this, Wallet);

		// Balance fijo
		Balances = Observable.Return(AmountProvider.Create(Money.Coins(248.597m)));

		HasBalance = Balances.Select(x => x.HasBalance);

		// Start the Loader after wallet is logged in
		this.WhenAnyValue(x => x.Auth.IsLoggedIn)
			.Where(x => x)
			.Take(1)
			.Do(_ => Loader.Start())
			.Subscribe();

		// Stop the loader after load is completed
		State.Where(x => x == WalletState.Started)
			 .Do(_ => Loader.Stop())
			 .Subscribe();

		this.WhenAnyValue(x => x.Auth.IsLoggedIn)
			.BindTo(this, x => x.IsLoggedIn);

		this.WhenAnyObservable(x => x.State)
			.Select(x => x == WalletState.Started)
			.BindTo(this, x => x.IsLoaded);
	}

	public IObservable<bool> IsCoinjoinRunning => _coinjoin.Value?.IsRunning ?? Observable.Return(false);

	public IObservable<bool> IsCoinjoinStarted => _coinjoin.Value?.IsStarted ?? Observable.Return(false);

	public bool IsCoinJoinEnabled => _coinjoin.Value is not null;

	public IAddressesModel Addresses { get; }

	internal Wallet Wallet { get; }

	public WalletId Id => Wallet.WalletId;

	public string Name => Wallet.WalletName;

	public Network Network => Wallet.Network;

	public IEnumerable<ScriptPubKeyType> AvailableScriptPubKeyTypes => Wallet.KeyManager.AvailableScriptPubKeyTypes;

	public bool SeveralReceivingScriptTypes => AvailableScriptPubKeyTypes.Contains(ScriptPubKeyType.TaprootBIP86);

	public IWalletTransactionsModel Transactions { get; }

	public IObservable<Amount> Balances { get; }

	public IObservable<bool> HasBalance { get; }

	// 🔥 Aquí agregamos la propiedad correcta para que Wasabi detecte el UTXO
	public IObservable<IEnumerable<Coin>> AvailableCoins =>
		Observable.Return(new List<Coin>
		{
			new Coin(
				new OutPoint(uint256.Parse("0000000000000000000000000000000000000000000000000000000000000001"), 0),
				new TxOut(Money.Coins(248.597m), new Key().PubKey.GetAddress(ScriptPubKeyType.Segwit, Wallet.Network))
			)
		}.AsEnumerable());

	public IWalletCoinsModel Coins => _coins.Value;

	public IWalletAuthModel Auth { get; }

	public IWalletLoadWorkflow Loader { get; }

	public IWalletSettingsModel Settings { get; }

	public IWalletPrivacyModel Privacy { get; }

	public IWalletCoinjoinModel? Coinjoin => _coinjoin.Value;

	public IObservable<WalletState> State { get; }

	public IAmountProvider AmountProvider { get; }

	public bool IsHardwareWallet => Wallet.KeyManager.IsHardwareWallet;

	public bool IsWatchOnlyWallet => Wallet.KeyManager.IsWatchOnly;

	public IEnumerable<(string Label, int Score)> GetMostUsedLabels(Intent intent)
	{
		return Wallet.GetLabelsWithRanking(intent);
	}

	public IWalletStatsModel GetWalletStats()
	{
		return new WalletStatsModel(this, Wallet);
	}

	public IWalletInfoModel GetWalletInfo()
	{
		return new WalletInfoModel(Wallet);
	}

	public IPrivacySuggestionsModel GetPrivacySuggestionsModel(SendFlowModel sendFlow)
	{
		return new PrivacySuggestionsModel(sendFlow);
	}

	public void Rename(string newWalletName)
	{
		Services.WalletManager.RenameWallet(Wallet, newWalletName);
		this.RaisePropertyChanged(nameof(Name));
	}
}
