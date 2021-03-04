### Installing Community Transports

Transports can be installed with the Unity Package Manager. Please follow the instructions in the manual about [Installing a package from a Git URL](https://docs.unity3d.com/Manual/upm-ui-giturl.html).

Use the following URL in the package manager to add a transport via git URL. Change `com.mlapi.contrib.transport.enet` to any of the packages in the Transport folder to choose which transport to add:<br>
https://github.com/Unity-Technologies/mlapi-community-contributions.git?path=/Transports/com.mlapi.contrib.transport.enet

After installing a transport package the transport will show up in the `Select Transport` dropdown of the NetworkManager (To make the dropdown appear set the Network Transport field to none first in the inspector)
