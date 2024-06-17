using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Plugin.Services;

namespace Chronofoil.Utility;

public static class Util
{
	public static unsafe T Read<T>(nint ptr) where T : struct
	{
		var size = Unsafe.SizeOf<T>();
		var span = new Span<byte>((void*)ptr, size);
		return MemoryMarshal.Cast<byte, T>(span)[0];
	}

	public static void LogBytes(IPluginLog log, ReadOnlySpan<byte> data, int offset, int length)
	{
		log.Debug($"{ByteString(data, offset, length)}");
	}

	public static string ByteString(ReadOnlySpan<byte> data, int offset, int length)
	{
		var sb = new StringBuilder();
		for (int i = offset; i < length; i++)
		{
			sb.Append($"{data[i]:X2}");
		}
		return sb.ToString();
	}
	
	public static unsafe string ByteString(byte* data, int offset, int length)
	{
		var sb = new StringBuilder();
		for (int i = offset; i < length; i++)
		{
			sb.Append($"{data[i]:X2}");
		}
		return sb.ToString();
	}
	
	public static string GetHumanByteString(ulong bytes)
	{
		return bytes switch
		{
			>= (1024 * 1024 * 1024) => $"{bytes / 1024 / 1024 / 1024}gb",
			>= (1024 * 1024) => $"{bytes / 1024 / 1024}mb",
			>= (1024) => $"{bytes / 1024}kb",
			_ => $"{bytes} bytes",
		};
	}
	
	public static TimeSpan ToTimeSpan(this TimeSpanIdentifier identifier)
	{
		return identifier switch
		{
			// TimeSpanIdentifier.Hours => TimeSpan.FromHours(1),
			TimeSpanIdentifier.Days => TimeSpan.FromDays(1),
			TimeSpanIdentifier.Weeks => TimeSpan.FromDays(7),
			TimeSpanIdentifier.Months => TimeSpan.FromDays(30),
			TimeSpanIdentifier.Years => TimeSpan.FromDays(365),
			_ => throw new ArgumentOutOfRangeException(nameof(identifier), identifier, null)
		};
	}

	public static TimeSpan Convert(int timeValue, TimeSpanIdentifier identifier)
	{
		return identifier.ToTimeSpan() * timeValue;
	}

	public static DateTime GetTime(DateTime now, int value, TimeSpanIdentifier identifier)
	{
		return now + Convert(value, identifier);
	}

	public static string GetTimeString(DateTime time, bool eosFlag)
	{
		return eosFlag ? "End of Service" : time.ToLocalTime().ToString(CultureInfo.CurrentCulture);
	}

	public static string GetRunningGameVersion()
	{
		var path = Environment.ProcessPath!;
		var parent = Directory.GetParent(path)!.FullName;
		var ffxivVerFile = Path.Combine(parent, "ffxivgame.ver");
		return File.Exists(ffxivVerFile) ? File.ReadAllText(ffxivVerFile) : "0000.00.00.0000.0000";
	}

	public static byte[] GetResource(string name)
	{
		var assembly = Assembly.GetExecutingAssembly();
		var resourceStream = assembly.GetManifestResourceStream(name);
		using var ms = new MemoryStream();
		if (resourceStream == null) throw new FileNotFoundException();
		resourceStream.CopyTo(ms);
		return ms.ToArray();
	}

	public static byte[] RandomBytes(int length)
	{
		var data = new byte[length];
		Random.Shared.NextBytes(data);
		return data;
	}

	public static string RandomByteString(int byteLength)
	{
		var data = RandomBytes(byteLength);
		var sb = new StringBuilder();
		foreach (var b in data)
			sb.Append($"{b:X2}");
		return sb.ToString();
	}

	// public static bool IsOnTitleScreen()
	// {
	// 	this.IsOpen = !this.clientState.IsLoggedIn;
	//
	// 	if (!this.configuration.ShowTsm)
	// 		this.IsOpen = false;
	//
	// 	var charaSelect = this.gameGui.GetAddonByName("CharaSelect", 1);
	// 	var charaMake = this.gameGui.GetAddonByName("CharaMake", 1);
	// 	var titleDcWorldMap = this.gameGui.GetAddonByName("TitleDCWorldMap", 1);
	// 	if (charaMake != IntPtr.Zero || charaSelect != IntPtr.Zero || titleDcWorldMap != IntPtr.Zero)
	// 		this.IsOpen = false;
	// }
}