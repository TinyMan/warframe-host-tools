using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WarframeHostTools
{
	internal class IpApiResponse
	{
		public string? Query { get; set; }
		public decimal? Lon { get; set; }
		public decimal? Lat { get; set; }
		public string? CountryCode { get; set; }
		public string? Country { get; set; }
		public string? RegionName { get; set; }
		public string? City { get; set; }
		public string? Isp { get; set; }
		public string? Org { get; set; }
		public string? As { get; set; }
		public string? AsName { get; set; }
		public bool? Mobile { get; set; }
		public bool? Proxy { get; set; }
		public bool? Hosting { get; set; }
	}
}
