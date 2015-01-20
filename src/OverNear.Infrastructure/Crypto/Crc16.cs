using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OverNear.Infrastructure
{
	public class Crc16
	{
		const ushort POLYNOMIAL = 0xA001;
		static readonly ushort[] TABLE = new ushort[256];

		static Crc16()
		{
			ushort value;
			ushort temp;
			for (ushort i = 0; i < TABLE.Length; ++i)
			{
				value = 0;
				temp = i;
				for (byte j = 0; j < 8; ++j)
				{
					if (((value ^ temp) & 0x0001) != 0)
					{
						value = (ushort)((value >> 1) ^ POLYNOMIAL);
					}
					else
					{
						value >>= 1;
					}
					temp >>= 1;
				}
				TABLE[i] = value;
			}
		}

		public static ushort ComputeChecksum(byte[] bytes)
		{
			ushort crc = 0;
			for (int i = 0; i < bytes.Length; ++i)
			{
				byte index = (byte)(crc ^ bytes[i]);
				crc = (ushort)((crc >> 8) ^ TABLE[index]);
			}
			return crc;
		}

		public static byte[] ComputeChecksumBytes(byte[] bytes)
		{
			ushort crc = ComputeChecksum(bytes);
			return BitConverter.GetBytes(crc);
		}
	}
}
