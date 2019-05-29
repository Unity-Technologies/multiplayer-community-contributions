# MLAPI.Transports
[![Discord](https://img.shields.io/discord/449263083769036810.svg?label=discord&logo=discord&color=informational)](https://discord.gg/FM8SE9E)
[![Build Status](https://img.shields.io/appveyor/ci/midlevel/mlapi-transports/master.svg?logo=appveyor)](https://ci.appveyor.com/project/MidLevel/mlapi-transports/branch/master)

[![Download](https://img.shields.io/badge/download-artifacts-informational.svg)](https://ci.appveyor.com/project/MidLevel/mlapi-transports)
[![Licence](https://img.shields.io/github/license/midlevel/mlapi.transports.svg?color=informational)](https://github.com/MidLevel/MLAPI.Transports/blob/master/LICENCE)

This is a collection repository for all official MLAPI transports. 

## Download
There are two ways to download transports for use with the MLAPI.

#### Installer
The latest transports can always be downloaded from the MLAPI installer. This is the prefered way.

#### Manually
You can download the latest versions from the [CI server](https://ci.appveyor.com/project/MidLevel/mlapi-transports), selecting the configuration and clicking artifacts. Then you can download a zip archive for the transport you want.

## Adding new transports
If you have a transport you wish to be added to the official list, fork the GitHub repository, follow the steps below and submit a pull request.

#### 1. Create the transport project
Create a new transport project in the project solution. The csproj file should be kept minimal. Always reference the MLAPI through NuGet and the Unity dlls from the Libraries folder. When possible, the transport should also be referenced via NuGet rather than binaries. Unless you have a very specific reason, create targets for net35, net45, net471 and netstandard2.0. Currently, net35 **or** net45 is **required**. An example csproj could look like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!--Needs to include net35 or net45. Include all of the below when possible-->
    <TargetFrameworks>net35;net45;net471;netstandard2.0</TargetFrameworks>
  </PropertyGroup>
  <ItemGroup>
    <!--When possible, reference the transport via NuGet-->
    <PackageReference Include="Ruffles" Version="2.0.1" />
    <!--Referene UnityEngine via the provided binary-->
    <Reference Include="UnityEngine, Version=0.0.0.0, Culture=neutral, processorArchitecture=MSIL">
      <HintPath>..\Libraries\Unity\UnityEngine.dll</HintPath>
      <SpecificVersion>False</SpecificVersion>
      <Private>false</Private>
    </Reference>
    <!--Use the latest MLAPI version -->
    <PackageReference Include="MLAPI" Version="10.2.3" />
  </ItemGroup>
</Project>
```


#### 2. Add the build to the CI
To make sure your transport gets automatically published, it has to be added to the CI. Do this by adding an entry in the ``appveyor.yml`` file to automatically zip your project after a build. Example:

```yml
after_build:
# ENET
  - 7z a ENET.zip %APPVEYOR_BUILD_FOLDER%\EnetTransport\bin\*\*\ENet-CSharp.dll %APPVEYOR_BUILD_FOLDER%\EnetTransport\bin\*\*\enet.dylib %APPVEYOR_BUILD_FOLDER%\EnetTransport\bin\*\*\enet.dll %APPVEYOR_BUILD_FOLDER%\EnetTransport\bin\*\*\libenet.so %APPVEYOR_BUILD_FOLDER%\EnetTransport\bin\*\*\EnetTransport*.*
# Ruffles
  - 7z a Ruffles.zip %APPVEYOR_BUILD_FOLDER%\RufflesTransport\bin\*\*\Ruffles.* %APPVEYOR_BUILD_FOLDER%\RufflesTransport\bin\*\*\RufflesTransport.*

```

Remember that every file that is added to the zip will be given to exported when installing the transport. You should therefor **NOT** include files such as MLAPI binaries, UnityEngine binaries or other unrelated files. For the transport, XML and PDB files should be included if possible.

#### 3. Installer Indexing
In order for the transport to be visible inside the editor, the transport has to be added to the artifact_paths.json file in the root of the repository. Example entry:

```json
{
    "id": "enet-csharp",
    "name": "ENET",
    "path": "ENET.zip",
    "description": "ENET is a lightweight reliable UDP library written in C.",
    "credits": "Albin Corén (transport adapter), Stanislav Denisov (fork and C# wrapper), Lee Salzman (original implementation).",
    "licence": "MIT/X11",
    "platform_compatibility_description": "Works on Windows, Linux and macOS by default. Other platforms needs native binaries compiled.",
    "net35": true,
    "net45": true,
    "preferNet45": false,
    "experimental": false,
    "mlapi_major_version": 10
}
```

All fields are required. See the explanation for each field below:

##### id
The id is the unique id of a transport. It shall avoid special characters, - and _ is allowed. This is not visible to the user except for in the folder structure. This should represent the unique version of the transport that is adapted. For example, the ENET transport is based on a fork called "ENet-CSharp", the id would then be "enet-csharp" while the name would simply be "ENET".

##### path
The path is the display name that the user sees. This should represent the underlying transport and not the transport adapter. For example, the ENET transport is based on a fork called "ENet-CSharp", the id would then be "enet-csharp" while the name would simply be "ENET".

##### path
The path is the name of the zip file as it's defined in the ``appveyor.yml`` file.

##### description
The description should describe what the core transport is about, include the protocols it operates under and core design goals such as "lightweight", "realtime" or other useful descriptors.

##### credits
The credits string should give credits to all notable parties involved in the creation of the transport. This includes the transport itself, any potential forks used and the transport adapter. This should contain the first and last name if the subject is a person, aliases and usernames are not allowed, or the full company name if it's a corporate entity.

##### licence
The licence string should contain the name of the licence used for the original transport. This should not include the licence for the transport adapter as all transport adapters are licenced under the MIT/X11 licence according to this repositories ``LICENCE`` file. If the licence does not have a common, well-known name a link to it should be provided. The full licence should **NOT** be used.

##### platform_compatibility_description
The platform_compatibility_description should describe what platform and/or runtime limitations a transport might have.

##### net35
The net35 flag describes if the transport has a NET35 target.

##### net45
The net45 flag describes if the transport has a NET45 target.

##### preferNet45
The preferNet45 flag describes if the transport benefits from being ran in NET45 as opposed to NET35.

##### experimental
The experimental flag describes if the transport or its adapter is in an experimental or work in progress and potentially unstable state. The key difference is that non experimental transports have the expectation of working properly.

##### mlapi_major_version
The mlapi_major_version integer should describe the MLAPI major version that is used for building this transport adapter. This **MUST** match the version that is imported via NuGet in the csproj file. Example: If the MLAPI NuGet version used is 10.2.3, the mlapi_major_version **MUST** be 10.