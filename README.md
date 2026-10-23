# conquest
A small roguelike shoot em up style game, inspired by the arcade game, Asteroids.


## Requirements
Map and Objects

    a map will generate when starting a new game
    a map will build non-active entities during generation
    a map will build active entities during generation
    non-active entities will not move or attack
    non-active entities will drop resources, useful to the player

 

Playable and Non-Playable Characters

    main character moves via WASD
    cursor moves via mouse
    bullets follow the trajectory of the cursor
    bullets will despawn after leaving the camera’s view
    The main character will rotate to point towards the direction of the cursor
    active entities will move and attack
    active entities will drop more resources than non-active entities

 

Camera and Game-loop Scaling

    active and non-active entities will be drawn when coming into the camera’s view
    camera will consist of two components: the window and the sensor. The window will be the screen in which the player can see the game. The sensor will be slightly larger than the window and will be checking which entities need drawn and undrawn. Both components of the camera will follow the player’s movement around the map. 
    When enough resources have been gathered the game will pause and award a random buff to the player, increase the level of the game, and increase the difficulty
    Higher levels will affect the spawn times of enemies, making the game gradually more difficult



