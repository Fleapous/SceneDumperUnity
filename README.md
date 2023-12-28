# SceneDumperUnity

SceneDumperUnity is a console tool that analyzes a Unity project directory and performs the following tasks:

- Dumps the Scene Hierarchy.
- Lists unused C# files.
- Lists unserialized C# files.

## Table of Contents

- [About](#about)
- [Getting Started](#getting-started)
- [Usage](#usage)
- [Important to Know](#important-to-know)

## About

SceneDumperUnity is a console tool designed for Unity developers. It helps you analyze your Unity project by providing insights into the Scene Hierarchy, listing unused assets, and identifying unserialized C# files.

## Getting Started

### Prerequisites

To run SceneDumperUnity, you need to have [.NET SDK](https://dotnet.microsoft.com/download) installed on your machine.

### Installation

1. Clone the repository:

    ```bash
    git clone https://github.com/Fleapous/SceneDumperUnity.git
    cd SceneDumperUnity
    ```

2. Build the project:

    ```bash
    dotnet build
    ```

## Usage
To use SceneDumperUnity, provide the `targetDir` and `outputDir` parameters when executing:

```bash
dotnet run -- targetDir outputDir
```
## Important to Know

- If you encounter problems, it could be due to the naming of objects in your Unity project. Try removing "-" and ":" characters from the names of the objects.
- This tool doesn't work with the Prefab system and will only recognize scripts that inherit from MonoBehaviour.
- The tool can handle nested script files, but it won't look recursively for scene files.
- For the tool to work, scene files must be located inside the `Assets/Scenes` directory, and scripts must be inside the `Assets/Scripts` directory.


