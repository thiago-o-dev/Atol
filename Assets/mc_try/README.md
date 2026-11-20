# **Fish Movement Shader**

A lightweight shader designed to add procedural swimming and body movement to fish models without requiring additional animation rigs or complex animation systems.

The shader can be used for underwater environments, aquariums, oceans, lakes, rivers, and stylized or realistic aquatic scenes.

### Features

* Procedural fish swimming animation
* Automatic body and tail movement
* Adjustable movement speed
* Adjustable movement intensity
* Optional variation/randomization
* Suitable for large numbers of fish
* Works without traditional skeletal animation
* Designed for real-time use
* Compatible with URP
* Customizable through Material properties
* Easy to update through Shader Graph



### Requirements

Unity Version:

\[It should work with every unity versions that supports Shader Graph]



Render Pipeline:

\[URP]



### Mesh Requirements

**The important part** is that the fish's forward direction follows the Z axis from positive to negative:

+Z  →  →  -Z

HEAD        TAIL



The fish should not be oriented along the X or Y axis.

Incorrect orientations may cause the shader deformation to behave incorrectly or make the fish swim in the wrong direction.



You can look at an example of how the material is used in a scene. It shows an object with and without armature. They have quite different variable values, and I can't tell you exactly what the values ​​will be for your objects.





**The quality of the deformation** depends heavily on the topology of the fish mesh.



A fish with only a few vertices along its body cannot bend smoothly.



Recommended topology

&#x20;      Head              Tail

&#x20;       ↓                  ↓

&#x20;   ┌────┬────┬────┬────┬────┐

&#x20;   │    │    │    │    │    │

&#x20;   └────┴────┴────┴────┴────┘



More subdivisions generally result in smoother deformation.



Not recommended

&#x20;   ┌────────────────────────┐

&#x20;   └────────────────────────┘



A very low-poly mesh may produce visible deformation artifacts.



## Description of variables

SwimWavelength: It can be either a positive or negative number and is responsible for the wavelength of rotation along the Z axis. If you see that the fish seems to be swimming towards the tail, then simply invert the value.



SwimSpeed: Speed of the animation.



SwimDistance: Range of the animation.



MovementOnAxisX: If you want add motion to X (up and down) axis just past value to this variable.



MainTex: Albedo texture.



Normal: NormalMap.



NormalStrength: NormalMap strength.



WobbleFrequency: If you want to add wobble animation. (Rotation on Z axis)



WobbleSpeed: animation speed of the wobble.



HeadMask: ANIMATION MASK. It masks the animation of the fish. For example: 0 - whole fish animated, 0.5 - animate tail fish and no head anim, 1 - no anim.



Color: Add color to the material.



### The fish bends in the wrong direction?



Check the model's local orientation and rotation.



Make sure the fish's forward direction matches Z axis. (Check out Samples in FishMovementScene mc\_try folder)



### The movement looks too rigid?



The fish mesh may not have enough subdivisions.



Try using a mesh with more geometry along the body and tail.





## FAQ

Does this shader require an Animator?

\-No.



Does it require a rig?

\-No, but object with rig and without is different for shader and variable values.



Can I use it with low-poly fish?

\-Yes, you can try, but I can’t guarantee the result, the main thing is the orientation of the object.



Can I use it with multiple fish?

\-Yes, but remember that completely different fish can use different variable values.



Does it work with GPU instancing?

\-I think so, but i didn't test it.



Can I use it for other aquatic creatures?

\-You can try, depends on creature.



Can I modify the shader?

\-Yes, just don't sell it.





##### LICENSE:

3D objects of the fish is not my, its from open source sketchfab model



You can use it in your commercial projects and games, without even mentioning me, but if you do, thank you.

&#x20;                           ,

&#x20;                      /(  /:./\\

&#x20;                   ,\_/:`-/::/::/\\\_ ,

&#x20;                   |:|::/::/::/::;'( ,,

&#x20;                   |:|:/::/::/:;'::,':(.( ,

&#x20;              \_,-'"HHHHH"""HHoo--.::::,-:(,----...\_

&#x20;           ,-"HHHb   mc\_try  HHHHb   HHHoo-.::::::::::-.

&#x20;         ,'   "HHHb  "HHHHb "HHHH   HHHHH  Hoo::::::::::.              \_,.-::`.

&#x20;       ,'      "HH`.  "HHHH  HHHHb  "HHHH  "HHHb`-:::::::.        \_.-:::::::;'

&#x20;      / ,-.        :   HHHH  "HHHH   HHHH   HHHH  Hoo::::;    \_,-:::::::::;'

&#x20;    ,'  `-'        :   HHHH   HHHH   "HHH   "HHH  "HHHH-:.\_,o:::::::::::;'

&#x20;   /               :   HHHH \_\_HHHP...........HHH   HHHF   HHH:::::::;:;'

&#x20;  /\_               ; \_,-::::::.:::::::::::::''HH   HHH    "HH::::::::(

&#x20;  (\_"-.,          /  : :.::.:.::::::::::,d   HHH   "HH     HH::::::::::.

&#x20;   (,-'          /    :.::.:::.::::::;'HHH   "HH    HH,::"-.H::::::::::::.

&#x20;    ".         ,'    : :.:::.::::::;'  "HH    HH   \_H-:::)   `-::::::::::::.

&#x20;      `-.\\\\\\\_\\\\\\\_\\\\\\\_,-'       `-.:::::,--'"     "H    HH,-::::::/        "--.::::::::.

&#x20;           """---..\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_\_,-------'::::::::;/               """---'

&#x20;                       \\::.:---.          `::::::::::;'

&#x20;                        \\::::::|            `::;-'""

&#x20;                         `.::::|

&#x20;                           `.::|

&#x20;                             `-'

