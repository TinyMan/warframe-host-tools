using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Automation;

namespace WarframeHostTools
{
	internal class Peer : INotifyPropertyChanged
	{
		private IPAddress? _ipAddress;

		public IPAddress? IPAddress
		{
			get { return _ipAddress; }
			set
			{
				_ipAddress = value;
				RaisePropertyChanged(nameof(IPAddress));
			}
		}

		private bool _isBlocked;

		public bool IsBlocked
		{
			get { return _isBlocked; }
			set
			{
				_isBlocked = value;
				RaisePropertyChanged(nameof(IsBlocked));
			}
		}
		private string? _Location;

		public string? Location
		{
			get { return _Location; }
			set
			{
				_Location = value;
				RaisePropertyChanged(nameof(Location));
			}
		}

		private DateTime? _lastPacket;

		public DateTime? LastPacket
		{
			get { return _lastPacket; }
			set
			{
				_lastPacket = value;
				RaisePropertyChanged(nameof(LastPacket));
			}
		}

		private int _packetCount;
		public int PacketCount
		{
			get { return _packetCount; }
			set
			{
				_packetCount = value;
				RaisePropertyChanged(nameof(PacketCount));
			}
		}


		private string? _AsName;
		public string? AsName
		{
			get { return _AsName; }
			set
			{
				_AsName = value;
				RaisePropertyChanged();
			}
		}
		private string? _As;
		public string? As
		{
			get { return _As; }
			set
			{
				_As = value;
				RaisePropertyChanged();
			}
		}
		private string? _Isp;
		public string? Isp
		{
			get { return _Isp; }
			set
			{
				_Isp = value;
				RaisePropertyChanged();
			}
		}
		private string? _Org;
		public string? Org
		{
			get { return _Org; }
			set
			{
				_Org = value;
				RaisePropertyChanged();
			}
		}
		private bool _Hosting;

		public bool Hosting
		{
			get { return _Hosting; }
			set
			{
				_Hosting = value;
				RaisePropertyChanged();
			}
		}
		private DateTime _FirstPacket;

		public DateTime FirstPacket
		{
			get { return _FirstPacket; }
			set
			{
				_FirstPacket = value;
				RaisePropertyChanged();
			}
		}


		public event PropertyChangedEventHandler? PropertyChanged;
		public void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChangedEventHandler? handler = PropertyChanged;
			if (handler != null)
			{
				handler(this, new PropertyChangedEventArgs(propertyName));
			}
		}
	}
}