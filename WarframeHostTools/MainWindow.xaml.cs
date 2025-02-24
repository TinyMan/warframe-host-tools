using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
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
			datagrid.ItemsSource = Peers;


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
	}
}