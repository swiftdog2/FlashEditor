# Test Project Setup Guide

Follow these steps to create and configure the test projects for this solution.

1. **Create the test project**
   - In Visual Studio select `Add -> New Project -> xUnit Test Project (.NET Framework)`.
   - Name the project `FlashEditor.Tests` and target **.NET Framework 4.7.2**.

2. **Add project reference**
   - Right click the new test project, choose **Add Reference**, and select the `FlashEditor` project.

3. **Install NuGet packages**
   - Open the Package Manager Console and run:
     ```
     Install-Package xunit
    Install-Package xunit.runner.visualstudio
    Install-Package Moq
    Install-Package Microsoft.NETFramework.ReferenceAssemblies.net472
    ```

4. **Organise folders and namespaces**
   - Mirror the source structure inside the test project under folders such as `Utils/`, `IO/`, and `Cache/Util/`.
   - Use namespaces that mirror the production namespaces with `.Tests` suffix, e.g. `FlashEditor.Tests.Utils`.

5. **Running the tests**
   - Build the solution and open the **Test Explorer** window.
   - All tests should appear automatically thanks to the xUnit Visual Studio runner.
   - Use `dotnet test` or `vstest.console.exe` if executing from command line.
   - If building on a machine without the .NET Framework developer packs, the
     `Microsoft.NETFramework.ReferenceAssemblies.net472` package provides the
     reference assemblies so `dotnet test` can compile the projects.

6. **CI Integration**
   - In your pipeline add a task similar to:
     ```yaml
     - script: dotnet test --configuration Release --no-build
       displayName: 'Run xUnit tests'
     ```
   - The build should fail automatically if any tests fail.
