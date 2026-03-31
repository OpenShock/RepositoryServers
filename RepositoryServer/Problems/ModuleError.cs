using System.Net;

namespace OpenShock.RepositoryServer.Problems;

public static class ModuleError
{
    public static OpenShockProblem ModuleNotFound => new OpenShockProblem("Module.NotFound", "The referenced module was not found", HttpStatusCode.NotFound);
    public static OpenShockProblem ZipInvalid => new OpenShockProblem("Module.ZipInvalid", "The uploaded file is not a valid zip archive");
    public static OpenShockProblem ZipEmpty => new OpenShockProblem("Module.ZipEmpty", "The uploaded zip archive contains no entries");
    public static OpenShockProblem ZipPathTraversal => new OpenShockProblem("Module.ZipPathTraversal", "The zip archive contains entries with path traversal sequences");
    public static OpenShockProblem ZipDisallowedDirectory(string dir) => new OpenShockProblem("Module.ZipDisallowedDirectory", $"The zip archive contains a disallowed root directory: '{dir}'. Only 'wwwroot' is permitted");
    public static OpenShockProblem ZipDisallowedRootFile(string file) => new OpenShockProblem("Module.ZipDisallowedRootFile", $"The zip archive contains a disallowed root file: '{file}'. Only .dll, .pdb, and .json files are allowed at the root");
}