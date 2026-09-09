using System.Collections.Generic;

namespace VideoOzet.Business.DTOs;

public class BranchDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public bool IsPopular { get; set; }
    public string? IconUrl { get; set; }
}

public class DistrictDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class CityDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<DistrictDto> Districts { get; set; } = new();
}
