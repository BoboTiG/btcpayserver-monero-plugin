using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Monero.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Monero.Services;

public class MoneroLoadUpService : IHostedService
{
    private const string CryptoCode = "XMR";
    private readonly ILogger<MoneroLoadUpService> _logger;
    private readonly MoneroRpcProvider _moneroRpcProvider;
    private readonly ISettingsRepository _settingsRepository;

    public MoneroLoadUpService(ILogger<MoneroLoadUpService> logger, MoneroRpcProvider moneroRpcProvider, ISettingsRepository settingsRepository)
    {
        _moneroRpcProvider = moneroRpcProvider;
        _logger = logger;
        _settingsRepository = settingsRepository;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Attempt to load existing wallet");

            string walletDir = _moneroRpcProvider.GetWalletDirectory(CryptoCode);
            if (!string.IsNullOrEmpty(walletDir))
            {
                var savedState = await _settingsRepository.GetSettingAsync<MoneroWalletState>();
                if (savedState?.PasswordFileMigration != true)
                {
                    await TryDeprecatePasswordFile();
                }
                await _moneroRpcProvider.OpenWallet(CryptoCode, "wallet", "");
                await _moneroRpcProvider.UpdateSummary(CryptoCode);
                _logger.LogInformation("Existing wallet successfully loaded");
            }
            else
            {
                _logger.LogInformation("No wallet directory configured, skipping wallet migration");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to load {CryptoCode} wallet. Error Message: {ErrorMessage}", CryptoCode,
                ex.Message);
        }
    }

    private async Task TryDeprecatePasswordFile()
    {
        try
        {
            string walletDir = _moneroRpcProvider.GetWalletDirectory(CryptoCode);
            string passwordFile = Path.Combine(walletDir, "password");
            string walletKeysFile = Path.Combine(walletDir, "wallet" + ".keys");

            if (!File.Exists(passwordFile))
            {
                _logger.LogInformation("No password file found during password deprecation");
                return;
            }

            if (!File.Exists(walletKeysFile))
            {
                _logger.LogWarning("Wallet file named {walletKeysFile} not found. Skipping password deprecation", walletKeysFile);
                return;
            }

            string password = (await File.ReadAllTextAsync(passwordFile)).Trim();
            await _moneroRpcProvider.OpenWallet(CryptoCode, "wallet", password);
            await _moneroRpcProvider.ChangeWalletPassword(CryptoCode, password, "");
            await _moneroRpcProvider.CloseWallet(CryptoCode);
            await _settingsRepository.UpdateSetting(new MoneroWalletState { PasswordFileMigration = true });
            _logger.LogInformation("Successfully migrated wallet to remove password");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during wallet password deprecation");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}