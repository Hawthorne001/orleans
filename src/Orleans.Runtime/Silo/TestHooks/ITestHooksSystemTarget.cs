using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Runtime.TestHooks
{
    internal interface ITestHooks
    {
        Task<SiloAddress> GetConsistentRingPrimaryTargetSilo(uint key);
        Task<string> GetConsistentRingProviderDiagnosticInfo();
        Task<string> GetServiceId();
        Task<bool> HasStorageProvider(string providerName);
        Task<bool> HasStreamProvider(string providerName);
        Task<int> UnregisterGrainForTesting(GrainId grain);
        Task<Dictionary<SiloAddress, SiloStatus>> GetApproximateSiloStatuses();
    }

    internal interface ITestHooksSystemTarget : ITestHooks, ISystemTarget
    {
    }
}
