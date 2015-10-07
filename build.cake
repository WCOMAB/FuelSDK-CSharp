///////////////////////////////////////////////////////////////////////////////
// ARGUMENTS
///////////////////////////////////////////////////////////////////////////////

var target          = Argument<string>("target", "Default");
var configuration   = Argument<string>("configuration", "Release");

///////////////////////////////////////////////////////////////////////////////
// GLOBAL VARIABLES
///////////////////////////////////////////////////////////////////////////////
var isLocalBuild        = !AppVeyor.IsRunningOnAppVeyor;
var isPullRequest       = AppVeyor.Environment.PullRequest.IsPullRequest;
var solutions           = GetFiles("./**/*.sln");
var solutionPaths       = solutions.Select(solution => solution.GetDirectory());
var releaseNotes        = ParseReleaseNotes("./ReleaseNotes.md");
var version             = releaseNotes.Version.ToString();
//var binDir              = "./FuelSDK-CSharp/FuelSDK-CSharp/bin/" + configuration;
var binDir              = "./FuelSDK-CSharp/bin/" + configuration;
var nugetRoot           = "./nuget/";
var semVersion          = isLocalBuild
                                ? version
                                : string.Concat(version, "-build-", AppVeyor.Environment.Build.Number);
var assemblyInfo        = new AssemblyInfoSettings {
                                Title                   = "FuelSDK-CSharp",
                                Description             = "FuelSDK-CSharp",
                                Product                 = "FuelSDK-CSharp",
                                Company                 = "Salesforce Fuel",
                                Version                 = version,
                                FileVersion             = version,
                                InformationalVersion    = semVersion,
                                Copyright               = string.Format("Copyright � {0}", DateTime.Now.Year),
                                CLSCompliant            = true,
                                ComVisible              = false
                            };
var nuGetPackSettings   = new NuGetPackSettings {
                                Id                      = assemblyInfo.Product,
                                Version                 = assemblyInfo.InformationalVersion,
                                Title                   = assemblyInfo.Title,
                                Authors                 = new[] {assemblyInfo.Company},
                                Owners                  = new[] {assemblyInfo.Company},
                                Description             = assemblyInfo.Description,
                                Summary                 = "Salesforce FUELSdk-CSharp",
                                ProjectUrl              = new Uri("https://github.com/WCOMAB/FuelSDK-CSharp"),
                                IconUrl                 = new Uri("http://cdn.rawgit.com/WCOMAB/nugetpackages/master/Chocolatey/icons/wcom.png"),
                                LicenseUrl              = new Uri("https://github.com/WCOMAB/FuelSDK-CSharp"),
                                Copyright               = assemblyInfo.Copyright,
                                ReleaseNotes            = releaseNotes.Notes.ToArray(),
                                Tags                    = new [] {"ExactTarget", "FuelSDK" },
                                RequireLicenseAcceptance= false,
                                Symbols                 = false,
                                NoPackageAnalysis       = true,
                                Files                   = new [] {
                                                                    new NuSpecContent {Source = "CSharpSDK.dll", Target = "lib" },
                                                                    //new NuSpecContent {Source = "Cake.Kudu.pdb"},
                                                                    //new NuSpecContent {Source = "Cake.Kudu.xml"}
                                                                 },
                                BasePath                = binDir,
                                OutputDirectory         = nugetRoot
                            };

if (!isLocalBuild)
{
    AppVeyor.UpdateBuildVersion(semVersion);
}

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////

Setup(() =>
{
    // Executed BEFORE the first task.
    Information("Running tasks...");

    var buildStartMessage = string.Format(
                            "Building version {0} of {1} ({2}).",
                            version,
                            assemblyInfo.Product,
                            semVersion
                            );

    Information(buildStartMessage);
});

Teardown(() =>
{
    // Executed AFTER the last task.
    Information("Finished running tasks.");
});

///////////////////////////////////////////////////////////////////////////////
// TASK DEFINITIONS
///////////////////////////////////////////////////////////////////////////////

Task("Clean")
    .Does(() =>
{
    // Clean solution directories.
    foreach(var path in solutionPaths)
    {
        Information("Cleaning {0}", path);
        CleanDirectories(path + "/**/bin/" + configuration);
        CleanDirectories(path + "/**/obj/" + configuration);
    }
});

Task("Restore")
    .Does(() =>
{
    // Add Cake MyGet Feed Source to be able to use pre-release
    if (!NuGetHasSource(
         "https://www.myget.org/F/cake"))
    {
        NuGetAddSource("Cake-MyGet", "https://www.myget.org/F/cake");
    }

    // Restore all NuGet packages.
    foreach(var solution in solutions)
    {
        Information("Restoring {0}...", solution);
        NuGetRestore(solution);
    }
});

Task("Build")
    .IsDependentOn("Clean")
    .IsDependentOn("Restore")
    .Does(() =>
{
    // Build all solutions.
    foreach(var solution in solutions)
    {
        Information("Building {0}", solution);
        MSBuild(solution, settings =>
            settings.SetPlatformTarget(PlatformTarget.MSIL)
                .UseToolVersion(MSBuildToolVersion.VS2015)
                .WithProperty("TreatWarningsAsErrors","false")
                .WithTarget("Build")
                .SetConfiguration(configuration));
    }
});


Task("Create-NuGet-Package")
    .IsDependentOn("Build")
    .Does(() =>
{
    if (!DirectoryExists(nugetRoot))
    {
        CreateDirectory(nugetRoot);
    }
    NuGetPack(nuGetPackSettings);
});

Task("Publish-MyGet")
    .IsDependentOn("Create-NuGet-Package")
    .WithCriteria(() => !isLocalBuild)
    .WithCriteria(() => !isPullRequest)
    .Does(() =>
{
    // Resolve the API key.
    var apiKey = EnvironmentVariable("MYGET_API_KEY");
    if(string.IsNullOrEmpty(apiKey)) {
        throw new InvalidOperationException("Could not resolve MyGet API key.");
    }

    var source = EnvironmentVariable("MYGET_INTERNAL_API_URL");
    if(string.IsNullOrEmpty(apiKey)) {
        throw new InvalidOperationException("Could not resolve MyGet source.");
    }

    // Get the path to the package.
    var package = nugetRoot + "FuelSDK-CSharp." + semVersion + ".nupkg";

    // Push the package.
    NuGetPush(package, new NuGetPushSettings {
        Source = source,
        ApiKey = apiKey
    });
});


Task("Default")
    .IsDependentOn("Create-NuGet-Package");

Task("AppVeyor")
    .IsDependentOn("Publish-MyGet");

///////////////////////////////////////////////////////////////////////////////
// EXECUTION
///////////////////////////////////////////////////////////////////////////////

RunTarget(target);
