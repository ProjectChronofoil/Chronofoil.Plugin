using System.IO;
using System.Text;

namespace Chronofoil.Utility;

public static class Extensions
{
	public static void WritePadded(this Stream stream, string str, int length)
	{
		var toWrite = str;
		var padding = 0;
		if (str.Length > length)
		{
			toWrite = str[..length];
		}
		else
		{
			padding = length - str.Length;
		}

		stream.Write(Encoding.ASCII.GetBytes(toWrite));
		for (int i = 0; i < padding; i++)
		{
			stream.WriteByte(0);
		}
	}
}