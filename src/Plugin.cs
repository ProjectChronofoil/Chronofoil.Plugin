using Chronofoil.Capture;
using Chronofoil.Capture.Context;
using Chronofoil.Capture.IO;
using Chronofoil.Capture.Session;
using Chronofoil.Censor;
using Chronofoil.Lobby;
using Chronofoil.UI;
using Chronofoil.UI.Components;
using Chronofoil.UI.Windows;
using Chronofoil.Utility;
using Chronofoil.Web;
using Chronofoil.Web.Auth;
using Chronofoil.Web.Info;
using Chronofoil.Web.Upload;
using Dalamud.Game;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chronofoil;

/// <summary>
/// Bootstrap class for the Chronofoil plugin.
/// </summary>
public class Plugin : IDalamudPlugin
{
	private readonly IHost _host;
	
	public Plugin(DalamudPluginInterface pi)
	{
		var builder = new HostBuilder();
		builder
			.UseContentRoot(pi.ConfigDirectory.FullName)
			.ConfigureServices((_, services) =>
		{
			var configuration = pi.GetPluginConfig() as Configuration ?? new Configuration();
			configuration.Initialize(pi);
			
			services
				.AddExistingService(pi)
				.AddExistingService(pi.UiBuilder)
				.AddExistingService(configuration)
				.AddDalamudService<IFramework>(pi)
				.AddDalamudService<IClientState>(pi)
				.AddDalamudService<ICommandManager>(pi)
				.AddDalamudService<IGameGui>(pi)
				.AddDalamudService<ISigScanner>(pi)
				.AddDalamudService<IGameInteropProvider>(pi)
				.AddDalamudService<IPluginLog>(pi)
				.AddDalamudService<INotificationManager>(pi)
				.AddDalamudService<ITitleScreenMenu>(pi)
				.AddSingleton<MultiSigScanner>()
				.AddSingleton<CaptureHookManager>()
				.AddSingleton<CaptureSessionManager>()
				.AddSingleton<ContextManager>()
				.AddSingleton<LobbyEncryptionProvider>()
				.AddSingleton<CaptureManager>()
				.AddSingleton<ChronofoilClient>()
				.AddSingleton<InfoService>()
				.AddSingleton<UploadService>()
				.AddSingleton<AuthManager>()
				.AddSingleton<UploadModal>()
				.AddSingleton<FaqTab>()
				.AddSingleton<CaptureTab>()
				.AddSingleton<UploadsTab>()
				.AddSingleton<SettingsTab>()
				.AddSingleton<MainWindow>()
				.AddSingleton<RegisterModal>()
				.AddSingleton<LoginModal>()
				.AddSingleton<NewTosModal>()
				.AddSingleton<TitleScreenWaiter>()
				.AddSingleton<ChronofoilUI>()
				.AddSingleton<OpcodeService>()
				.AddSingleton<Chronofoil>();
		});
		
		_host = builder.Build();
		_host.Start();
		
		// initialization of singletons
		_host.Services.GetService<Chronofoil>();
		_host.Services.GetService<CaptureSessionManager>();
		_host.Services.GetService<ContextManager>();
		_host.Services.GetService<OpcodeService>();
	}
	
	public void Dispose()
	{
		_host?.StopAsync().GetAwaiter().GetResult();
		_host?.Dispose();
	}
}