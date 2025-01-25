# Sharing AutoCAD Plant 3D Projects to Autodesk Construction Cloud (ACC)

The `CLIPLANT360SHARE` command is a CLI version of the `_PLANTPROJECTCOLLABORATION` command in Plant3D. It automates the process of uploading a project to ACC with document mangement from Plant3D Desktop application.

The project provides a facilitiy to convert a SQL Server-based Plant3D projects to SQLite format.

To use the CLI command:

1. Open the Plant3D command line.

2. Type `CLIPLANT360SHARE` and press Enter.

## Features

- **SQL Server to SQLite Conversion**: Converts Plant3D project files from SQL Server format to SQLite format.

- **Project Upload to ACC**: Uploads the converted project to the Autodesk Construction Cloud (ACC) for collaboration.

- **Command-Line Interface (CLI)**: Provides a CLI command (`CLIPLANT360SHARE`) to automate the upload process.

## Prerequisites

Before using the `CLIPLANT360SHARE` tool, ensure that you have the following:

- **Autodesk Plant3D 2025**: The tool is designed to work within the Autodesk Plant3D environment.

- **Autodesk Construction Cloud (ACC) Account**: You need an ACC account with the necessary permissions to upload projects.

- **.NET 8.0**: The tool is written in C# and requires the .NET 8.0 to run.

## Building the Project

- **Clone the Repository**: Clone the repository to your local machine.
  
  ```bash
  git clone https://github.com/MadhukarMoogala/CLIPLANT360SHARE.git
  cd CLIPLANT360SHARE
  devenv P360Share.csproj
  ```

- **Open the Project**: Open the project in Visual Studio 2022

- **Edit The Project** : Edit `.csproj` and repath `AcadInstallDir` to the AutoCAD instal directory on your machine.

- **Build the Project**: Build the project in Visual Studio by selecting `Build` > `Build Solution` or by pressing `Ctrl+Shift+B`.

- **Run the Project**: Once the project is built, launch AutoCAD Plant3D, open any local Plant Project, `NETLOAD` the binary dll and run `CLIPLANT360SHARE`

### Uploading a Project to ACC

With out command, you can also use the `UploadProject` method to upload a Plant3D project to the Autodesk Construction Cloud (ACC), . This method takes the following parameters:

- `hubName`: The name of the ACC hub.

- `projectId`: The ID of the ACC project.

- `cts`: A cancellation token to cancel the operation.

```csharp
P360Share.Program.UploadProject(
    "Developer Advocacy Support", 
    "b.1549f155-5acf-4359-a496-f734a2ab05dd", 
    new CancellationToken()
);
```
## Demo


https://github.com/user-attachments/assets/54b2c13a-9e08-4437-bac5-fa00e80ad388

## Known Limitations
- The CLI command works best when a Plant 3D project is opened in AutoCAD Plant 3D. Loading a project.xml file directly using the API, e.g., PlantProject prj = PlantProject.LoadProject(@"D:\abc\project.xml", true, null, null);, does not synchronize effectively with the collaboration cache.
- Ensure that the ACC Hub contains at least one project; otherwise, you may encounter a null ProjectFolderUrn exception.

## License

This project is licensed under the MIT License. See the [LICENSE](https://github.com/MadhukarMoogala/CLIPLANT360SHARE/blob/main/LICENSE) file for more details.

This sample is provided by A D N Support for educational purposes.
