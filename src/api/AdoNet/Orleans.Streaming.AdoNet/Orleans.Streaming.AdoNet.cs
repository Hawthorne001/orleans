//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------
namespace Orleans.Configuration
{
    public partial class AdoNetStreamOptions
    {
        [Redact]
        public string ConnectionString { get { throw null; } set { } }

        public System.TimeSpan DeadLetterEvictionTimeout { get { throw null; } set { } }

        public int EvictionBatchSize { get { throw null; } set { } }

        public System.TimeSpan EvictionInterval { get { throw null; } set { } }

        public System.TimeSpan ExpiryTimeout { get { throw null; } set { } }

        public System.TimeSpan InitializationTimeout { get { throw null; } set { } }

        public string Invariant { get { throw null; } set { } }

        public int MaxAttempts { get { throw null; } set { } }

        public System.TimeSpan VisibilityTimeout { get { throw null; } set { } }
    }

    public partial class AdoNetStreamOptionsValidator : IConfigurationValidator
    {
        public AdoNetStreamOptionsValidator(AdoNetStreamOptions options, string name) { }

        public void ValidateConfiguration() { }
    }
}

namespace Orleans.Hosting
{
    public partial class ClusterClientAdoNetStreamConfigurator : ClusterClientPersistentStreamConfigurator
    {
        public ClusterClientAdoNetStreamConfigurator(string name, IClientBuilder clientBuilder) : base(default!, default!, default!) { }

        public ClusterClientAdoNetStreamConfigurator ConfigureAdoNet(System.Action<Microsoft.Extensions.Options.OptionsBuilder<Configuration.AdoNetStreamOptions>> configureOptions) { throw null; }

        public ClusterClientAdoNetStreamConfigurator ConfigureCache(int cacheSize = 4096) { throw null; }

        public ClusterClientAdoNetStreamConfigurator ConfigurePartitioning(int partitions = 8) { throw null; }
    }

    public static partial class ClusterClientAdoNetStreamExtensions
    {
        public static IClientBuilder AddAdoNetStreams(this IClientBuilder builder, string name, System.Action<Configuration.AdoNetStreamOptions> configureOptions) { throw null; }

        public static IClientBuilder AddAdoNetStreams(this IClientBuilder builder, string name, System.Action<ClusterClientAdoNetStreamConfigurator> configure) { throw null; }
    }

    public partial class SiloAdoNetStreamConfigurator : SiloPersistentStreamConfigurator
    {
        public SiloAdoNetStreamConfigurator(string name, System.Action<System.Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>> configureDelegate) : base(default!, default!, default!) { }

        public SiloAdoNetStreamConfigurator ConfigureAdoNet(System.Action<Microsoft.Extensions.Options.OptionsBuilder<Configuration.AdoNetStreamOptions>> configureOptions) { throw null; }

        public SiloAdoNetStreamConfigurator ConfigureCache(int cacheSize = 4096) { throw null; }

        public SiloAdoNetStreamConfigurator ConfigurePartitioning(int partitions = 8) { throw null; }
    }

    public static partial class SiloBuilderAdoNetStreamExtensions
    {
        public static ISiloBuilder AddAdoNetStreams(this ISiloBuilder builder, string name, System.Action<Configuration.AdoNetStreamOptions> configureOptions) { throw null; }

        public static ISiloBuilder AddAdoNetStreams(this ISiloBuilder builder, string name, System.Action<SiloAdoNetStreamConfigurator> configure) { throw null; }
    }
}