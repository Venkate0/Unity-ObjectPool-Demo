# Unity-ObjectPool-Demo

A small, complete top-down shooter built to demonstrate **production-quality object pooling** in Unity — a generic, pre-warmed, hard-capped `ObjectPool<T>` with live statistics, driven by a wave-based enemy demo scene.

Built with C# and Unity 2022.3 LTS (works on any 2021.3+ editor — Unity will offer to upgrade the project on open).

---

## What This Demonstrates

| Concept | Where in code |
|---|---|
| Generic pool with interface constraint (`where T : MonoBehaviour, IPoolable`) | [`ObjectPool.cs`](Assets/Scripts/Pooling/ObjectPool.cs) |
| Pool lifecycle contract (`OnSpawn` / `OnDespawn`) | [`IPoolable.cs`](Assets/Scripts/Pooling/IPoolable.cs) |
| Type-agnostic pool stats for tooling (`IPoolInfo`) | [`IPoolInfo.cs`](Assets/Scripts/Pooling/IPoolInfo.cs) |
| Self-returning pooled base class with double-return guard | [`PooledObject.cs`](Assets/Scripts/Pooling/PooledObject.cs) |
| Singleton pool ownership, prefab-keyed pool registry | [`PoolManager.cs`](Assets/Scripts/Pooling/PoolManager.cs) |
| Pre-warming (constructor `initialSize`) | `ObjectPool` constructor |
| Hard cap with `Debug.LogWarning` and null return | `ObjectPool.Get()` |
| `Stack<T>` (LIFO) for available objects, with reasoning | `ObjectPool.cs` header comment, decision #1 |
| Bulk despawn for wave resets (`ReturnAll`) | `ObjectPool.ReturnAll()` / `PoolManager.ReturnAllPools()` |
| Pool stats (`ActiveCount`, `AvailableCount`, `TotalCount`) | `ObjectPool` properties; consumed by `PoolDebugUI` and `EnemySpawner` |
| Pooled enemy AI (chase, damage, hit flash, death, lifetime failsafe) | [`EnemyController.cs`](Assets/Scripts/Gameplay/EnemyController.cs) |
| Pooled projectiles with miss-expiry (pool-drain prevention) | [`ProjectileController.cs`](Assets/Scripts/Gameplay/ProjectileController.cs) |
| Coroutine-based escalating waves, pool-stat wave-clear detection | [`EnemySpawner.cs`](Assets/Scripts/Gameplay/EnemySpawner.cs) |
| Scene-view spawn radius Gizmo | `EnemySpawner.OnDrawGizmos()` |
| CharacterController movement + mouse aim via layer-masked raycast | [`PlayerController.cs`](Assets/Scripts/Gameplay/PlayerController.cs) |
| Jitter-free smooth camera (`LateUpdate` + `SmoothDamp`) | [`CameraFollow.cs`](Assets/Scripts/Gameplay/CameraFollow.cs) |
| Zero-setup debug overlay with FPS + per-pool stats | [`PoolDebugUI.cs`](Assets/Scripts/UI/PoolDebugUI.cs) |
| Hit flash without material instantiation (`MaterialPropertyBlock`) | `EnemyController.cs`, decision #3 |
| No `FindObjectOfType` in hot paths (static published reference) | `PlayerController.Instance` + `EnemyController.OnSpawn()` |
| Allocation discipline (cached `WaitForSeconds`, reused buffers) | `EnemySpawner.cs`, `EnemyController.cs`, `ObjectPool._returnBuffer` |

---

## Why Not Instantiate/Destroy?

`Instantiate()` and `Destroy()` look free. They are not:

1. **Instantiate is slow.** Cloning a prefab means allocating the GameObject, every component, deserializing serialized fields, registering with physics/rendering, and firing `Awake`/`OnEnable`. Doing this for 8 projectiles per second plus a wave of enemies causes visible frame-time spikes at exactly the busiest gameplay moments.

2. **Destroy creates garbage.** Every destroyed object becomes work for the garbage collector. C#'s GC (especially the non-generational Boehm GC used by Mono/IL2CPP in most Unity versions) is **stop-the-world**: when enough garbage accumulates, the GC pauses *every* thread mid-frame to scan the heap. That's the classic Unity hitch — the game runs at 60 fps, then drops one 80 ms frame "randomly". It isn't random; it's your Instantiate/Destroy churn getting collected.

3. **Heap fragmentation.** Repeated alloc/free cycles of differently-sized objects fragment the managed heap, making future allocations slower and the heap grow larger than the live data actually needs.

**Pooling fixes all three** by paying the instantiation cost once, up front, during loading:

- Spawning becomes `Stack.Pop()` + `SetActive(true)` — nanoseconds, zero allocation.
- Despawning becomes `SetActive(false)` + `Stack.Push()` — zero garbage.
- The heap stays flat. Open the Profiler (Window → Analysis → Profiler), watch **GC Alloc** on the CPU module while playing this demo: spawning enemies and bullets after warm-up allocates nothing.

**Proof in-game:** press **F1** and watch the `Total` count per pool. After the opening seconds it stops growing forever — every enemy and bullet you see from then on is a recycled object.

---

## Scene Setup (step by step)

The repository contains all scripts plus minimal project files. Scene and prefabs are created by hand below — about 10 minutes.

### 0. Open the project

1. Open **Unity Hub → Add → Add project from disk** and select the `Unity-ObjectPool-Demo` folder.
2. Open it with **Unity 2022.3 LTS** (or newer — accept the upgrade prompt).
3. Create a scene: **File → New Scene** (Basic/3D), save it as `Assets/Scenes/PoolDemo.unity`.

### 1. Create the "Ground" layer

1. In the top-right of the Inspector, click the **Layers** dropdown → **Edit Layers…**
2. Under **Layers**, click the field next to **User Layer 6** and type `Ground`.

### 2. Ground plane

1. **GameObject → 3D Object → Plane**, rename it `Ground`.
2. Transform: Position `(0, 0, 0)`, Scale `(5, 1, 5)` (a 50×50 unit floor).
3. In the Inspector's top-right **Layer** dropdown, set the layer to **Ground**.
4. (Optional) Create a material (`Assets/Materials/M_Ground`, any dark color) and drag it onto the plane.

### 3. Player

1. **GameObject → 3D Object → Capsule**, rename it `Player`. Position `(0, 1, 0)`.
2. **Remove** its `Capsule Collider` (Add Component menu ⋮ → Remove Component) — the CharacterController replaces it.
3. **Add Component → Character Controller.** Defaults are fine (Center `(0,0,0)`, Radius `0.5`, Height `2`).
4. Add a child to show facing: right-click `Player` → **3D Object → Cube**, rename `Barrel`, Position `(0, 0.25, 0.7)`, Scale `(0.2, 0.2, 0.8)`. Remove the Barrel's `Box Collider`.
5. Add another empty child: right-click `Player` → **Create Empty**, rename `Muzzle`, Position `(0, 0.25, 1.2)`. This is where projectiles spawn.
6. **Add Component → Player Controller** (script) on `Player`. Set fields:

   | Field | Value |
   |---|---|
   | Move Speed | `6` |
   | Ground Stick Force | `4` |
   | Ground Layer | **Ground** (only) |
   | Max Aim Distance | `200` |
   | Min Aim Distance | `0.1` |
   | Projectile Prefab | *(assign in step 4)* |
   | Muzzle | drag the `Muzzle` child here |
   | Shots Per Second | `8` |
   | Muzzle Fallback Offset | `1` |
   | Projectile Pool Initial Size | `30` |
   | Projectile Pool Max Size | `100` |
   | Reset Key | `R` |
   | Spawner | *(assign in step 6)* |

### 4. Projectile prefab

1. **GameObject → 3D Object → Sphere**, rename `Projectile`. Scale `(0.3, 0.3, 0.3)`.
2. On its `Sphere Collider`, check **Is Trigger**.
3. **Add Component → Rigidbody**: check **Is Kinematic**, uncheck **Use Gravity**.
4. **Add Component → Projectile Controller.** Fields: Speed `20`, Max Lifetime `3`, Damage `1`.
5. (Optional) Bright material, e.g. yellow `M_Projectile`.
6. Drag `Projectile` from the Hierarchy into an `Assets/Prefabs/` folder to create the prefab, then **delete the scene copy**.
7. Back on `Player` → Player Controller, assign **Projectile Prefab** = the `Projectile` prefab asset.

### 5. Enemy prefab

1. **GameObject → 3D Object → Cube**, rename `Enemy`. Scale `(1, 1, 1)`, Position anywhere (it gets deleted).
2. Keep its `Box Collider` (leave **Is Trigger** unchecked; the projectile's trigger + kinematic rigidbody handles overlap events).
3. **Add Component → Enemy Controller.** Fields:

   | Field | Value |
   |---|---|
   | Move Speed | `3.5` |
   | Turn Speed | `720` |
   | Stop Distance | `1.2` |
   | Max Health | `3` |
   | Max Lifetime | `30` |
   | Flash Color | red |
   | Flash Duration | `0.1` |
   | Color Property Name | `_Color` (Built-in pipeline) — use `_BaseColor` if your project is URP |

4. Create a material `M_Enemy` (e.g. green) and assign it, so the red flash is visible.
5. Drag into `Assets/Prefabs/` to create the prefab, **delete the scene copy**.

### 6. PoolManager and EnemySpawner

1. **GameObject → Create Empty**, rename `PoolManager`, Position `(0, 0, 0)`. **Add Component → Pool Manager.**
   - You may leave *Pre-configured Pools* empty (the spawner and player create their pools in code), **or** add two entries to pre-declare them: `Enemy` (initial `20`, max `60`) and `Projectile` (initial `30`, max `100`).
2. **GameObject → Create Empty**, rename `EnemySpawner`, Position `(0, 0.5, 0)`. **Add Component → Enemy Spawner.** Fields:

   | Field | Value |
   |---|---|
   | Enemy Prefab | the `Enemy` prefab asset |
   | Initial Pool Size | `20` |
   | Max Pool Size | `60` |
   | Base Enemies Per Wave | `5` |
   | Enemies Added Per Wave | `3` |
   | Time Between Waves | `4` |
   | Time Between Spawns | `0.25` |
   | Spawn Radius | `15` |

   With the spawner selected (or Gizmos enabled), an orange ring in the Scene view shows the spawn radius.
3. On `Player` → Player Controller, assign **Spawner** = the `EnemySpawner` object.

### 7. Camera

1. Select **Main Camera**. **Add Component → Camera Follow.** Fields:

   | Field | Value |
   |---|---|
   | Target | the `Player` object |
   | Offset | `(0, 16, -8)` |
   | Smooth Time | `0.15` |
   | Look At Target | ✔ |

   (The script positions the camera at runtime — its starting transform doesn't matter.)

### 8. Debug overlay

1. **GameObject → Create Empty**, rename `PoolDebugUI`. **Add Component → Pool Debug UI.**
2. Fields: Toggle Key `F1`, Start Visible ✔, Fps Smoothing `0.1`, Panel Width `340`, Screen Padding `10`, Spawner = the `EnemySpawner` object.

### 9. Play

Press **Play**. Enemies stream in from the ring's edge, chase you, flash red when shot, and die after 3 hits. Waves escalate. Watch the F1 overlay: pool totals plateau and never grow again — that's the pool doing its job.

---

## Controls

| Input | Action |
|---|---|
| **W / A / S / D** | Move |
| **Mouse** | Aim (player faces the cursor's point on the ground) |
| **Left Mouse Button** (hold) | Shoot |
| **R** | Reset — return all pooled objects, restart at wave 1, re-center player |
| **F1** | Toggle the pool stats / FPS overlay |

---

## Key Design Decisions

Every file also carries a design-decision block in its header; the big ones:

### Stack vs Queue for available objects
`Stack<T>` (LIFO) hands back the most recently returned instance — the one whose memory is most likely still warm in the CPU cache — instead of cycling through the entire pool the way a FIFO `Queue<T>` would. LIFO also leaves the pool's "cold tail" untouched, which makes over-provisioned `initialSize` values visible in the hierarchy. Queues buy fairness/round-robin, which pooling has no use for.

### initialSize vs maxSize
They answer different questions. `initialSize` = "how many do I pay for during loading?" — size it to your *typical* peak so gameplay never triggers an `Instantiate`. `maxSize` = "what's the absolute worst case I'll tolerate?" — a memory ceiling and a leak alarm. The gap between them is a lazy-growth buffer: the pool grows on demand up to the cap, logging a warning each time so under-provisioning is visible in the console rather than invisible in the frame time.

### Why Get() returns null at the cap instead of force-recycling
Force-recycling (stealing the oldest active object) silently teleports a live object out from under its own systems — an enemy vanishes mid-fight, a bullet disappears mid-flight — producing bugs with no stack trace. Returning `null` plus a `Debug.LogWarning` makes starvation *loud* and pushes the decision to the caller, who has context: the spawner skips the enemy (a slightly smaller wave), the player drops the shot. Fail loud beats fail weird. If a design truly needs "newest wins" (e.g. bullet casings), that belongs in a caller-level policy, not silently inside the pool.

### Return-callback injection instead of pool references in objects
A pooled instance can't hold a typed `ObjectPool<T>` reference without becoming generic itself (which breaks Unity serialization). Instead the pool injects one `System.Action` per instance at creation time — one closure allocation ever, not per spawn — and `PooledObject.ReturnToPool()` invokes it blindly. Objects stay ignorant of pool internals; the pool keeps all bookkeeping.

### Double-return protection, twice
`PooledObject.IsSpawned` no-ops repeat `ReturnToPool()` calls within one spawn cycle (the projectile-hits-two-colliders case), and `ObjectPool.Return()` independently rejects anything not in its active set. Defense in depth against the single most common pooling bug.

### Wave-clear detection via pool stats
`EnemySpawner` waits on `pool.ActiveCount == 0` instead of maintaining a parallel "alive enemies" counter or a death-event bus. The pool is already the source of truth for what's alive — the stats properties aren't just for the debug overlay.

### MaterialPropertyBlock for the hit flash
`renderer.material` silently instantiates a material copy per enemy (breaking batching, leaking until scene unload). `MaterialPropertyBlock` overrides the color per-renderer with zero material instances — and matters double under pooling, where instances live for the whole session.

### Singleton scope
`PoolManager` and `PlayerController.Instance` are **scene-scoped** (no `DontDestroyOnLoad`, cleared in `OnDestroy`). Pooled instances are scene objects; a manager surviving a scene load would hold a graveyard of destroyed references.

### Atomic file writes
This demo has no save/load, so there is nothing to write to disk. If persistence were added, writes would go to a temp file followed by `File.Replace` (atomic on the same volume) so a crash mid-write can never corrupt an existing save.

---

## Project Structure

```
Unity-ObjectPool-Demo/
├── Assets/
│   └── Scripts/
│       ├── Pooling/
│       │   ├── IPoolable.cs           # Spawn/despawn lifecycle contract
│       │   ├── IPoolInfo.cs           # Type-agnostic stats + ReturnAll
│       │   ├── ObjectPool.cs          # Generic pre-warmed, capped pool
│       │   ├── PooledObject.cs        # Self-returning base class
│       │   └── PoolManager.cs         # Singleton pool registry
│       ├── Gameplay/
│       │   ├── PlayerController.cs    # WASD + mouse aim + shoot + reset
│       │   ├── EnemyController.cs     # Chase, damage, flash, despawn
│       │   ├── ProjectileController.cs# Fly, hit, expire
│       │   ├── EnemySpawner.cs        # Escalating coroutine waves
│       │   └── CameraFollow.cs        # Smooth LateUpdate follow
│       └── UI/
│           └── PoolDebugUI.cs         # F1 overlay: pool stats + FPS
├── Packages/manifest.json
├── ProjectSettings/ProjectVersion.txt
├── .gitignore
└── README.md
```

---

## .gitignore

The included [.gitignore](.gitignore) is the standard Unity ignore set: `Library/`, `Temp/`, `Logs/`, `obj/`, `Build(s)/`, `UserSettings/`, IDE folders, and OS junk are excluded; `Assets/`, `Packages/`, and `ProjectSettings/` (with their `.meta` files) are what belongs in version control.
