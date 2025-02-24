namespace WarframeHostTools
{
	public class FreeIpApiResponse
	{
		public string? IpAddress { get; set; }
		public decimal? Latitude { get; set; }
		public decimal? Longitude { get; set; }
		public string? CountryCode { get; set; }
		public string? CountryName { get; set; }
		public string? RegionName { get; set; }
		public string? CityName { get; set; }
		public bool? IsProxy { get; set; }

	}
}
