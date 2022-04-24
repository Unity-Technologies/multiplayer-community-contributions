## ClientNetworkAnimator

ClientNetworkAnimator is a simple script modified from the official ``NetworkAnimator``. It gives the ability that the clients can change their animation states and synchronize through the net automatically.

## Notice

Just like the official ``ClientNetworkTransform``, Syncing information from a client means that you put your trust on the client-side. This may impose state to the server. So, make sure no security-sensitive features use this animator.

## How to use

### Setup
1. Replace the official ``NetworkAnimator`` with `ClientNetworkAnimator`.

1. Make sure there is a ``NetworkObject`` component attached to this game object.

### Changing animation state
* ``SetInteger`` can be used by the owner directly.
* ``SetFloat`` can be used by the owner directly.
* ``SetBool`` can be used by the owner directly.
* ``SetTrigger`` can only be used by calling ``_clientNetworkAnimator.SetTrigger``.

## Code sample

```
using Netcode.Extensions;
using Unity.Netcode;
using UnityEngine;

public class Player : NetworkBehaviour
{
    private Animator _animator;
    private ClientNetworkAnimator _clientNetworkAnimator;
    
    // Start is called before the first frame update
    void Start()
    {
        _animator = GetComponent<Animator>();
        _clientNetworkAnimator = GetComponent<ClientNetworkAnimator>();
    }

    // Update is called once per frame
    void Update()
    {
        if (IsOwner)
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                //trigger can only be set by calling the clientNetworkAnimator
                _clientNetworkAnimator.SetTrigger("Jump");
            }
            if (Input.GetKeyDown(KeyCode.R))
            {
                //other parmas can be set directly
                _animator.SetBool("R",true);
                _animator.SetInteger("int",2);
                _animator.SetFloat("float",3.5f);
            }
        }
    }
}

```



## Known issue

### ``Blend Tree`` animation will not synchronized

Basically, the blend tree itself is a standalone state. Changing the blend value will not cause the state change so the blend tree can not be checked by the script. 

The official ``NetworkAnimator`` can not sync ``Blend Tree``, either.
Further research is in progress.

