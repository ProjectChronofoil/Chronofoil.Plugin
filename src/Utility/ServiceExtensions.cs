using Dalamud.IoC;
using Dalamud.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace Chronofoil.Utility;

public static class ServiceExtensions
{
	public static IServiceCollection AddDalamudService<T>(this IServiceCollection collection, DalamudPluginInterface pi) where T : class
	{
		var wrapper = new DalamudServiceWrapper<T>(pi);
		collection.AddSingleton(wrapper.Service);
		collection.AddSingleton(pi);
		return collection;
	}
	
	public static IServiceCollection AddExistingService<T>(this IServiceCollection collection, T service) where T : class
	{
		collection.AddSingleton(service);
		return collection;
	}
}

class DalamudServiceWrapper<T>
{
	[PluginService]
	[RequiredVersion("1.0")]
	public T Service { get; private set; } = default!;

	public DalamudServiceWrapper(DalamudPluginInterface pi)
	{
		pi.Inject(this);
	}
}