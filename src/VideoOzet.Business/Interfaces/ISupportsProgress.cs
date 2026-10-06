using System;

namespace VideoOzet.Business.Interfaces;

public interface ISupportsProgress
{
    Action<int, int>? OnProgress { get; set; }
}
