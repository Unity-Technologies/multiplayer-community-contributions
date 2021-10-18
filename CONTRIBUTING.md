# Contributing

Thank you for your interest in contributing to the Multiplayer Community Contributions repository.

Here are our guidlines for contributing:

* [Code of Conduct](#coc)
* [Ways to Contribute](#ways)
* [Issues and Bugs](#issue)
* [New Content and Features](#feature)
* [Unity Contribution Agreement](#cla)

## <a name="coc"></a> Code of Conduct

Please help us keep Unity Multiplayer Networking open and inclusive. Read and follow our [Code of Conduct](https://github.com/Unity-Technologies/com.unity.netcode.gameobjects/blob/main/CODE_OF_CONDUCT.md).

## <a name="ways"></a> Ways to Contribute

### <a name="issue"></a> Issues and Bugs

If you find a bug in the source code, you can help us by submitting an issue to our
GitHub Repository.

### <a name="feature"></a> New Content and Features

If you would like to add new content to the contributions repository or improve existing content create a Pull Request. 

#### Creating a new Netcode for GameObjects Transport
- Clone the multiplayer-community-contributions repository
- Copy the com.community.netcode.transport.template folder
- Rename the folder to better reflect your transport's name.
- Update CHANGELOG.md, package.json, README.md with information about your transport
- Rename TemplateTransport.cs and implement your transport. Rename the .asmdef file to the name of your transport as well.
- If your transport needs any additional user actions to run please specific them in README.md

#### Adding an extension to the com.community.netcode.extensions package
- Create a new folder in the extension's package runtime folder.
- Add your code to the folder.
- Put a short README.md into the folder explaining what your extension does and how to use it.


## <a name="cla"></a> Contributor License Agreements

When you open a pull request, you will be asked to enter into Unity's License Agreement which is based on The Apache Software Foundation's contribution agreement. We allow both individual contributions and contributions made on behalf of companies. We use an open source tool called CLA assistant. If you have any questions on our CLA, please submit an issue