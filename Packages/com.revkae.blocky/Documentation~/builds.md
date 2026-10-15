# Builds and platforms

The workspace, the compiler and the virtual machine are all runtime code, with no `UnityEditor` in them. Players build and run programs in a build exactly as they do in the Editor's Play mode. Only the menus under **Blocky** and **GameObject › Blocky**, and the Program Editor window, are Editor-only.

## IL2CPP and code stripping

Blocky makes its block classes by reflection, and Newtonsoft JSON fills program data by reflection, so nothing in the code names them. Without help, an IL2CPP build above *Minimal* stripping would remove them.

The package's `Runtime/link.xml` keeps the `Blocky.Runtime` and `Blocky.Data` assemblies whole, and Blocky hands that file to the linker for every build itself. There is nothing to copy and nothing to set, however the package was installed.

**Your own blocks** live in your assemblies, which that file doesn't cover. Keep the `[Preserve]` attribute that **Blocky › New Block…** puts on each block class and its constructor. A block class written by hand needs it too:

```csharp
[BlockExecutor("game.jump")]
[Preserve]
public sealed class JumpOp : IBlockOp
{
    [Preserve]
    public JumpOp() { }

    // ...
}
```

## WebGL

Blocky's code runs in a WebGL build like any other.

In-game saves go to `Application.persistentDataPath`, which a WebGL build keeps in the browser's own storage for your site. Clearing the browser's site data deletes them. Check that saves survive a reload in the browsers you target before you rely on them, or keep programs in storage of your own (see [Saving and loading](saving.md#your-own-storage)).

## Render pipelines

Blocky works with the **Universal Render Pipeline** and the **Built-in Render Pipeline**. The High Definition Render Pipeline has not been tested.

- **The workspace** is UI Toolkit, which draws the same in every pipeline.
- **Speech bubbles** are drawn with Blocky's own two shaders, in the package's `Runtime/Resources/Blocky` folder. They are unlit and look the same in URP and the Built-in pipeline, so there is nothing to set up. Because they sit in a `Resources` folder, they go into every build.
- **`Change Color`** sets a material's `_BaseColor` (URP) and `_Color` (the Built-in pipeline) — whichever the shader has. URP Lit and Unlit and the Built-in Standard shader all have one. A custom shader without either property doesn't change color. Sprites are tinted through their own color instead.

## 2D scenes

Blocky works on a flat 2D stage as well as in 3D.

- **Motion.** An object with a `SpriteRenderer`, `Collider2D` or `Rigidbody2D` moves on the XY plane: forward is its red (X) axis, the way a sprite faces, and turning is around Z. Set the runner's *Motion* field to *Two D* or *Three D* to decide for yourself.
- **Clicks and picking.** `When Clicked` and picking an object in the workspace find 3D colliders, 2D colliders, and sprites with no collider at all.
- **Contacts.** 2D collisions and triggers reach `When Collided`, `touching?` and `touching object?`, as 3D ones do. As in 3D, Unity reports a contact only when one of the two objects has a `Rigidbody2D`.
- **Speech bubbles** are drawn above the sprites.

A typical 2D setup: an orthographic camera looking down Z, and sprites with a `BoxCollider2D` — plus a `Rigidbody2D` on the ones that should bump into things.

2D support needs Unity's 2D physics module (`com.unity.modules.physics2d`), which 2D projects have by default. Without it, sprites still move on the XY plane, but 2D colliders aren't seen.

## Input

Blocky reads the mouse, pen, touchscreen and keyboard through the Input System, whichever was used last. *Active Input Handling* must be *Input System Package (New)* or *Both* (**Edit › Project Settings › Player**). `When Key Pressed` and `key pressed?` need a keyboard.
