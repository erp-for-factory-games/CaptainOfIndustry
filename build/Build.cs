using Fallout.Common;
using Fallout.Common.IO;
using Fallout.Common.ProjectModel;
using Fallout.Common.Tooling;
using Fallout.Common.Tools.DotNet;

/// <summary>
/// Fallout build for the Captain of Industry libraries and CLI tool.
///
///   <c>./build.sh Compile</c>  — restore + build
///   <c>./build.sh Test</c>     — the above, plus tests
///   <c>./build.sh Pack</c>     — nupkgs into <c>artifacts/packages</c>
///   <c>./build.sh Push --nuget-source &lt;url&gt; --nuget-api-key &lt;key&gt;</c>
///
/// The API key comes from nuget.org trusted publishing (OIDC) in CI, so it is
/// short-lived and never stored — see the publish job in .github/workflows.
/// </summary>
class Build : FalloutBuild
{
    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build — Debug for local, Release for CI.")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution] readonly Solution Solution = null!;

    [Parameter("NuGet feed to publish to.")]
    readonly string? NuGetSource;

    [Parameter("API key for the NuGet feed.")]
    [Secret]
    readonly string? NuGetApiKey;

    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PackagesDirectory => ArtifactsDirectory / "packages";

    Target Clean => _ => _
        .Executes(() =>
        {
            ArtifactsDirectory.CreateOrCleanDirectory();
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetTasks.DotNetRestore(s => s.SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetTasks.DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            // The tests that need Captain of Industry installed skip themselves
            // when it isn't — so this is the same command locally and on CI.
            // The two node-reuse variables are not cosmetic. Fallout waits for
            // the child's output streams to reach EOF, and MSBuild's reusable
            // worker nodes and build server outlive `dotnet test` while still
            // holding the inherited stdout handle — so EOF never arrives and the
            // build is killed with exit 143 long after every test has passed.
            // This bit the main ERP repo on 10.4.0; disabling the daemons lets
            // the pipes close.
            DotNetTasks.DotNetTest(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetProcessEnvironmentVariable("MSBUILDDISABLENODEREUSE", "1")
                .SetProcessEnvironmentVariable("DOTNET_CLI_USE_MSBUILD_SERVER", "0"));
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Produces(PackagesDirectory / "*.nupkg")
        .Executes(() =>
        {
            // Pack the solution and let each project decide via IsPackable: the
            // three libraries and the CLI tool pack, the test projects opt out.
            DotNetTasks.DotNetPack(s => s
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(PackagesDirectory)
                .EnableNoBuild()
                .EnableNoRestore());
        });

    Target Push => _ => _
        .DependsOn(Pack)
        .Requires(() => NuGetSource)
        .Requires(() => NuGetApiKey)
        .Executes(() =>
        {
            // SkipDuplicate so re-running a release tag is harmless: a published
            // version can never be replaced, only added to.
            foreach (var package in PackagesDirectory.GlobFiles("*.nupkg"))
            {
                DotNetTasks.DotNetNuGetPush(s => s
                    .SetTargetPath(package)
                    .SetSource(NuGetSource)
                    .SetApiKey(NuGetApiKey)
                    .EnableSkipDuplicate());
            }
        });
}
