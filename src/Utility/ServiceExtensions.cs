using Dalamud.IoC;
using Dalamud.Plugin;
using Microsoft.Extensions.DependencyInjection;

namespace Chronofoil.Utility;

public static class ServiceExtensions
{
	public static IServiceCollection AddDalamudService<T>(this IServiceCollection collection, IDalamudPluginInterface pi) where T : class
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
	public T Service { get; private set; } = default!;

	public DalamudServiceWrapper(IDalamudPluginInterface pi)
	{
		pi.Inject(this);
	}
}