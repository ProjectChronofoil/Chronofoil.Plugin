using System;
using System.Net.Http.Headers;
using System.Reflection;
using System.Threading.Tasks;
using Chronofoil.Capture;
using Chronofoil.Capture.Context;
using Chronofoil.Capture.IO;
using Chronofoil.Capture.Session;
using Chronofoil.Censor;
using Chronofoil.Common;
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
using Refit;

namespace Chronofoil;

/// <summary>
/// Bootstrap class for the Chronofoil plugin.
/// </summary>
public class Plugin : IDalamudPlugin
{
	private readonly IHost _host;
	
	public Plugin(IDalamudPluginInterface pi)
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
				.AddExistingService(configuration);

			services
				.AddDalamudService<IFramework>(pi)
				.AddDalamudService<IClientState>(pi)
				.AddDalamudService<ICommandManager>(pi)
				.AddDalamudService<IGameGui>(pi)
				.AddDalamudService<IChatGui>(pi)
				.AddDalamudService<ISigScanner>(pi)
				.AddDalamudService<ITextureProvider>(pi)
				.AddDalamudService<IGameInteropProvider>(pi)
				.AddDalamudService<IPluginLog>(pi)
				.AddDalamudService<INotificationManager>(pi)
				.AddDalamudService<ITitleScreenMenu>(pi);

			services
				.AddRefitClient<IChronofoilClient>(new RefitSettings
				{
					ExceptionFactory = _ => Task.FromResult<Exception?>(null)
				})
				.ConfigureHttpClient(c =>
				{
					c.BaseAddress = new Uri("https://cf-stg.perchbird.dev");
					var version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
					var header = new ProductInfoHeaderValue("Chronofoil.Plugin", version);
					c.DefaultRequestHeaders.UserAgent.Add(header);
				});
				
			services
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
				.AddSingleton<AutoUploadService>()
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