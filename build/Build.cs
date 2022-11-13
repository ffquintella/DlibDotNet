using System;
using System.IO;
using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Utilities.Collections;
using Serilog;
using static Nuke.Common.EnvironmentInfo;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

class Build : NukeBuild
{
    
    AbsolutePath SourceDirectory => RootDirectory / "src" ;
    AbsolutePath OutputDirectory => RootDirectory / "output";
    AbsolutePath OutputBuildDirectory => RootDirectory / "output" / "build";
    AbsolutePath OutputPackDirectory => RootDirectory / "output" / "package";
    AbsolutePath OutputPublishDirectory => RootDirectory / "output" / "publish";

    [GitRepository] readonly GitRepository Repository;

    string Version => Repository?.Tags?.FirstOrDefault() ?? "0.0.0";

    public static int Main () => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [Solution("DlibDotNet.sln")]
    readonly Solution Solution;

    [PathExecutable]
    readonly Tool Pwsh;


    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            // Collect and delete all /obj and /bin directories in all sub-directories
            var deletableDirectories = SourceDirectory.GlobDirectories("**/obj", "**/bin");
            foreach (var deletableDirectory in deletableDirectories)
            {
                if (!deletableDirectory.ToString().Contains("build"))
                {
                    Log.Information($"Deleting {deletableDirectory}");
                    Directory.Delete(deletableDirectory, true);
                }

            }
            if (Directory.Exists(OutputDirectory))
                Directory.Delete(OutputDirectory, true);
        });

    Target CleanPkg => _ => _
        .Executes(() =>
        {

            if (Directory.Exists(OutputPackDirectory))
                Directory.Delete(OutputPackDirectory, true);
        });

    Target Print => _ => _
        .Executes(() =>
        {
            Log.Information("STARTING BUILD");
            Log.Information("SOURCE DIR: {0}", SourceDirectory);
            Log.Information("OUTPUT DIR: {0}", OutputDirectory);


            Log.Information("Commit = {Value}", Repository.Commit);
            Log.Information("Branch = {Value}", Repository.Branch);
            Log.Information("Tags = {Value}", Repository.Tags);

            Log.Information("main branch = {Value}", Repository.IsOnMainBranch());
            Log.Information("main/master branch = {Value}", Repository.IsOnMainOrMasterBranch());

            Log.Information("VersionInfo = {Value}", Version);

            Log.Information("Solution path = {Value}", Solution);
            Log.Information("Solution directory = {Value}", Solution.Directory);

        });

    Target Prepare => _ => _
        .Before(Restore)
        .DependsOn(Print)
        .Executes(() =>
        {
            if (!Directory.Exists(OutputDirectory)) Directory.CreateDirectory(OutputDirectory);
            if (!Directory.Exists(OutputBuildDirectory)) Directory.CreateDirectory(OutputBuildDirectory);
            if (!Directory.Exists(OutputPackDirectory)) Directory.CreateDirectory(OutputPackDirectory);
            if (!Directory.Exists(OutputPublishDirectory)) Directory.CreateDirectory(OutputPublishDirectory);
        });


    Target Restore => _ => _
        .DependsOn(Print)
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution)
                .SetVerbosity(DotNetVerbosity.Normal));
        });

    Target Compile => _ => _
        .DependsOn(Print)
        .DependsOn(Prepare)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Log.Information($"Compiling {Solution.Name}");
            Log.Information($"Version {Version}");
            Log.Information($"On {Configuration.ToString()}");
            Log.Information($"To {OutputBuildDirectory}");

            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetVersion(Version)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(OutputBuildDirectory)
                .SetVerbosity(DotNetVerbosity.Normal));

        });

    Target PeparePack => _ => _
        .DependsOn(Print)
        .DependsOn(CleanPkg)
        .Executes(() =>
        {
            var project = Solution.GetProject("DlibDotNet");

            DotNetPublish(s => s
                .SetProject(project)
                .SetVersion(Version)
                .SetConfiguration(Configuration.Release)
                .SetRuntime("win")
                .EnablePublishTrimmed()
                .SetOutput(OutputPackDirectory)
                .SetVerbosity(DotNetVerbosity.Normal));

            Pwsh(
                arguments: $"Build.ps1 Release cuda 64 desktop 118",
                workingDirectory: SourceDirectory / "DlibDotNet.Native");

            File.Copy(SourceDirectory / "DlibDotNet.Native" / "build_win_desktop_cuda-118_x64" / "Release" / "DlibDotNetNative.dll",
                OutputPackDirectory / "DlibDotNetNative.dll");

            Pwsh(
                arguments: $"Build.ps1 Release cuda 64 desktop 118",
                workingDirectory: SourceDirectory / "DlibDotNet.Native.Dnn");

            File.Copy(SourceDirectory / "DlibDotNet.Native.Dnn" / "build_win_desktop_cuda-118_x64" / "Release" / "DlibDotNetNativeDnn.dll",
                OutputPackDirectory / "DlibDotNetNativeDnn.dll");

            File.Copy(SourceDirectory / ".." / "nuget" / "nuspec" / "DlibDotNet.CUDA-118.nuspec",
                OutputPackDirectory / "DlibDotNet.CUDA-118.nuspec");

            //File.Copy(Path.Combine(OutputBuildDirectory, "DlibDotNet.dll"), Path.Combine(OutputPackDirectory, "DlibDotNet.dll"));


        });
}
