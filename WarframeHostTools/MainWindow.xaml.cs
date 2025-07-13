using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using WindowsFirewallHelper;
using WindowsFirewallHelper.Addresses;
using WindowsFirewallHelper.FirewallRules;

namespace WarframeHostTools
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		private LibPcapLiveDeviceList devices;
		private LibPcapLiveDevice device;
		private ObservableCollection<Peer> Peers = new ObservableCollection<Peer>();
		private HttpClient httpClient = new HttpClient();
		private HashSet<IPAddress> IPAddresses = new HashSet<IPAddress>();
		private JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions
		{
			AllowTrailingCommas = true,
			PropertyNameCaseInsensitive = true,
			WriteIndented = true,
		};
		public const ushort PORT1 = 4950;
		public const ushort PORT2 = 4955;
		private const string FW_RULE_NAME_IN = "Warframe kick (in)";
		private const string FW_RULE_NAME_OUT = "Warframe kick (out)";
		private readonly FirewallWASRule InRule;
		private readonly FirewallWASRule OutRule;
		private Dictionary<IPAddress, string> PlayerNameByIP = new Dictionary<IPAddress, string>();
		private Dictionary<string, string> PlayerNameByMM = new Dictionary<string, string>();
		private Dictionary<string, IPAddress> IPByMM = new Dictionary<string, IPAddress>();
		private string? punchthrough_failure_addr = null;
		private Thread _logReaderThread;

		private ICollectionView ItemList;

		public MainWindow()
		{
			InitializeComponent();
			devices = LibPcapLiveDeviceList.Instance;


			interfaces.ItemsSource = devices;

			var defaultInterfaceAddress = App.Configuration["DefaultInterfaceAddress"];

			if (defaultInterfaceAddress != null)
			{
				var defaultInterface = IPAddress.Parse(defaultInterfaceAddress);

				interfaces.SelectedItem = devices.FirstOrDefault(d => d.Addresses.FirstOrDefault()?.Addr.ipAddress.Equals(defaultInterface) ?? false);
			}
			var _itemSourceList = new CollectionViewSource() { Source = Peers };
			ItemList = _itemSourceList.View;
			ItemList.Filter = FilterServers;
			datagrid.ItemsSource = ItemList;


			InRule = FirewallWAS.Instance.Rules.FirstOrDefault(r => r.Name == FW_RULE_NAME_IN)!;
			if (InRule == null)
			{
				InRule = FirewallWAS.Instance.CreateApplicationRule(FW_RULE_NAME_IN, FirewallAction.Block, FirewallDirection.Inbound, null, FirewallProtocol.UDP);
				InRule.IsEnable = false;
				FirewallWAS.Instance.Rules.Add(InRule);
			}
			else
			{
				InRule.IsEnable = false;
			}

			OutRule = FirewallWAS.Instance.Rules.FirstOrDefault(r => r.Name == FW_RULE_NAME_OUT)!;
			if (OutRule == null)
			{
				OutRule = FirewallWAS.Instance.CreateApplicationRule(FW_RULE_NAME_OUT, FirewallAction.Block, FirewallDirection.Outbound, null, FirewallProtocol.UDP);
				OutRule.IsEnable = false;
				FirewallWAS.Instance.Rules.Add(OutRule);
			}
			else
			{
				OutRule.IsEnable = false;
			}

			_logReaderThread = new Thread(LogReader);
			_logReaderThread.Start();
		}

		public string GetNameByIP(IPAddress ip)
		{
			return PlayerNameByIP.GetValueOrDefault(ip) ?? "-";
		}
		private void LogReader()
		{
			var localAppData = Environment.GetEnvironmentVariable("localappdata")!;
			var path = Path.Combine(localAppData, "Warframe");
			var wh = new AutoResetEvent(false);
			try
			{
				var fsw = new FileSystemWatcher(path);
				fsw.Filter = "EE.log";
				fsw.EnableRaisingEvents = true;
				fsw.Changed += (s, e) => wh.Set();

				var fs = new FileStream(Path.Combine(path, "EE.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
				using (var sr = new StreamReader(fs))
				{
					var s = "";
					while (true)
					{
						s = sr.ReadLine();
						if (s != null)
							ProcessLogLine(s);
						else
							wh.WaitOne(1000);
					}
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.ToString());
			}
			finally
			{
				wh.Close();
			}
		}

		private void ProcessLogLine(string line)
		{
			try
			{
				int i = line?.IndexOf("]: ") ?? -1;
				if (i == -1)
				{
					return;
				}
				var msg = line![(i + 3)..];
				if (msg.StartsWith("AddSquadMember: "))
				{
					var name_end = msg.IndexOf(", mm=", 16) - 1;
					var name = msg.Substring(16, name_end - 16);
					var mm_end = msg.IndexOf(", squadCount=", name_end);
					var mm = msg.Substring(name_end + 6, mm_end - (name_end + 6));
					PlayerNameByMM[mm] = name;
					if (IPByMM.TryGetValue(mm, out var ip))
					{
						SetNameForIp(ip, name);
					}

				}
				else if (msg.StartsWith("VOIP: Registered remote player "))
				{
					string data = msg.Substring(31);
					var sep = data.IndexOf(" (");
					var mm = data.Substring(0, sep);
					var ip_end = data.IndexOf(")");
					var ip = data.Substring(sep + 2, ip_end - sep - 2);
					if (IPEndPoint.TryParse(ip, out var ipEndpoint))
					{
						IPByMM[mm] = ipEndpoint.Address;
						if (PlayerNameByMM.TryGetValue(mm, out var name))
						{
							SetNameForIp(ipEndpoint.Address, name);
						}
					}
				}
				else if (msg.StartsWith("Failed to punch-through to "))
				{
					punchthrough_failure_addr = msg.Substring(53, msg.Length - 3);
				}
				else if (msg.StartsWith("VOIP: punch-through failure for "))
				{
					string mm = msg.Substring(32, msg.Length - 32 - 2);
					if (!string.IsNullOrEmpty(punchthrough_failure_addr) &&
						IPEndPoint.TryParse(punchthrough_failure_addr, out var ipEndpoint))
					{
						punchthrough_failure_addr = null;
						IPByMM[mm] = ipEndpoint.Address;
						if (PlayerNameByMM.TryGetValue(mm, out var name))
						{
							SetNameForIp(ipEndpoint.Address, name);
						}
					}
				}
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e.ToString());
			}

			void SetNameForIp(IPAddress ip, string name)
			{
				PlayerNameByIP[ip] = name;
				var peer = Peers.FirstOrDefault(p => p.IPAddress?.Equals(ip) ?? false);
				if (peer != null)
				{
					peer.Name = name;
				}
			}
		}

		private async Task LookupIp_FreeIpApi(IPAddress address, Peer peer)
		{
			string url = $"https://freeipapi.com/api/json/{address}";
			HttpResponseMessage response = await httpClient.GetAsync(url);
			string responseBody = await response.Content.ReadAsStringAsync();

			var ipInfo = JsonSerializer.Deserialize<FreeIpApiResponse>(responseBody, jsonSerializerOptions);

			Trace.WriteLine(responseBody);


			peer.Location = $"{ipInfo?.CityName}, {ipInfo?.RegionName}, {ipInfo?.CountryName}";
		}

		private async Task LookupIp_IpApi(IPAddress address, Peer peer)
		{
			// https://ip-api.com/docs/api:json#test
			var url = "http://ip-api.com/json/" + address.ToString() + "?fields=21229535";
			HttpResponseMessage response = await httpClient.GetAsync(url);
			string responseBody = await response.Content.ReadAsStringAsync();

			var ipInfo = JsonSerializer.Deserialize<IpApiResponse>(responseBody, jsonSerializerOptions)!;

			peer.Location = $"{ipInfo?.City}, {ipInfo?.RegionName}, {ipInfo?.Country}";
			peer.AsName = ipInfo.AsName;
			peer.As = ipInfo.As;
			peer.Org = ipInfo.Org;
			peer.Isp = ipInfo.Isp;
			peer.Hosting = ipInfo.Hosting ?? false;
			App.Current.Dispatcher.Invoke(() => ItemList.Refresh());
		}

		private void btnBlock_Click(object sender, RoutedEventArgs e)
		{
			var peer = ((sender as Button)?.DataContext as Peer);

			if (peer != null)
			{
				ToggleBlock(peer);

				peer.IsBlocked = !peer.IsBlocked;
			}
		}

		private void ToggleBlock(Peer peer)
		{
			Toggle(peer, InRule);
			Toggle(peer, OutRule);

			static void Toggle(Peer peer, FirewallWASRule rule)
			{
				var addresses = rule.RemoteAddresses.ToList();
				if (peer.IsBlocked)
				{
					addresses.RemoveAll(a => peer.IPAddress?.Equals(a) ?? false);
				}
				else
				{
					if (!rule.IsEnable)
					{
						rule.IsEnable = true;
					}
					addresses.Add(new SingleIP(peer.IPAddress));
					addresses.RemoveAll(a => a.ToString() == "*");
				}
				if (addresses.Count == 0)
				{
					rule.IsEnable = false;
				}
				rule.RemoteAddresses = addresses.ToArray();
			}
		}

		private void btnStart_Click(object sender, RoutedEventArgs e)
		{
			device?.Dispose();
			Peers.Clear();
			IPAddresses.Clear();
			device = (LibPcapLiveDevice)interfaces.SelectedItem;
			device.OnPacketArrival += Device_OnPacketArrival;
			device.Open(mode: DeviceModes.Promiscuous | DeviceModes.DataTransferUdp | DeviceModes.NoCaptureLocal);
			var address = device.Addresses.FirstOrDefault()?.Addr.ipAddress.ToString();
			device.Filter = $"udp and (udp port {PORT1} or udp port {PORT2}) and src net {address}";
			device.StartCapture();
			Trace.WriteLine($"Capture started on {device.Addresses.FirstOrDefault()?.Addr.ipAddress} at {DateTime.Now:HH:mm:ss}");
		}

		private void AddPeer(IPAddress address, DateTime date)
		{
			if (address != null)
			{
				var existing = Peers.FirstOrDefault(p => p.IPAddress?.Equals(address) ?? false);

				if (existing != null)
				{
					existing.PacketCount++;
					existing.LastPacket = date;
				}
				else if (!IPAddresses.Contains(address))
				{
					IPAddresses.Add(address);

					var peer = new Peer();
					peer.IPAddress = address;
					peer.FirstPacket = date;
					peer.PacketCount = 1;
					peer.LastPacket = date;
					if (PlayerNameByIP.TryGetValue(address, out var name))
					{
						peer.Name = name;
					}
					App.Current.Dispatcher.Invoke(() => Peers.Add(peer));

					LookupIp_IpApi(address, peer).ConfigureAwait(false);
				}
			}
		}

		private void Device_OnPacketArrival(object sender, PacketCapture e)
		{
			var packet = e.GetPacket().GetPacket();

			if (packet is EthernetPacket ethPacket && ethPacket.HasPayloadPacket)
			{
				if (ethPacket.PayloadPacket is IPPacket ipPacket && ipPacket.HasPayloadPacket)
				{
					if (ipPacket.PayloadPacket is UdpPacket udpPacket &&
						(udpPacket.SourcePort == 4950 || udpPacket.SourcePort == 4955) &&
						ipPacket.SourceAddress.Equals(device.Addresses.FirstOrDefault()?.Addr.ipAddress))
					{
						AddPeer(ipPacket.DestinationAddress, e.Header.Timeval.Date);
					}
				}
			}

		}

		private void btnStop_Click(object sender, RoutedEventArgs e)
		{
			device?.Dispose();
		}

		private void Window_Closed(object sender, EventArgs e)
		{
			_logReaderThread.Abort();
			InRule.IsEnable = false;
			OutRule.IsEnable = false;
			//FirewallWAS.Instance.Rules.Remove(rule);
			device?.Dispose();
			httpClient.Dispose();
		}

		private void btnUnblockAll_Click(object sender, RoutedEventArgs e)
		{
			foreach (var peer in Peers)
			{
				InRule.IsEnable = false;
				InRule.RemoteAddresses = new IAddress[] { };
				OutRule.IsEnable = false;
				OutRule.RemoteAddresses = new IAddress[] { };
				peer.IsBlocked = false;
			}
		}

		private void chkServers_Checked(object sender, RoutedEventArgs e)
		{
			ItemList.Filter = null;
		}

		private void chkServers_Unchecked(object sender, RoutedEventArgs e)
		{
			ItemList.Filter = FilterServers;
		}

		private bool FilterServers(object peer)
		{
			return !((peer as Peer)?.Hosting ?? false);
		}

	}
}