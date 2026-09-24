using Blocky.Runtime.Triggers;
using UnityEngine;

namespace Blocky.Runtime.Ops
{
    /// <summary>
    /// <c>sensing.distance_to</c> — how far this object is from another, in world units. An object no name matches
    /// answers 0: a learner who mistypes a name should see nothing happen, not have something fly to infinity.
    /// </summary>
    [BlockExecutor("sensing.distance_to")]
    public sealed class DistanceToValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var other = BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target);
            if (other == null || ctx.Target == null || other == ctx.Target) return BlockValue.Number(0f);
            return BlockValue.Number(Vector3.Distance(ctx.Target.transform.position, other.transform.position));
        }
    }

    /// <summary><c>sensing.position_of</c> — another object's x, y or z. The axis choice is read by index (x, y, z), as everywhere else.</summary>
    [BlockExecutor("sensing.position_of")]
    public sealed class PositionOfValue : IValueOp
    {
        public BlockValue Evaluate(ref SlotContext ctx)
        {
            var other = BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target);
            if (other == null) return BlockValue.Number(0f);

            var position = other.transform.position;
            return BlockValue.Number(ctx.Params[1].ChoiceIndex switch { 1 => position.y, 2 => position.z, _ => position.x });
        }
    }

    /// <summary><c>sensing.touching_object</c> — in contact with that one object, rather than with anything wearing a tag.</summary>
    [BlockExecutor("sensing.touching_object")]
    public sealed class TouchingObjectCondition : IConditionOp
    {
        public bool Evaluate(ref SlotContext ctx) =>
            BlockCollisionRelay.IsTouchingObject(ctx.Target, BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target));
    }

    /// <summary>
    /// <c>motion.point_towards</c> — turns to face another object. Yaw only: the direction is flattened onto the
    /// ground plane first, so an object pointing at something above or below it leans no further than a person
    /// turning to look would. Directly overhead leaves it as it was, since there is no direction to turn towards.
    /// On a 2D stage (<see cref="BlockyPlane"/>) it turns around Z instead, so its X axis — a sprite's facing — points at the other.
    /// </summary>
    [BlockExecutor("motion.point_towards")]
    public sealed class PointTowardsOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var other = BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target);
            if (other == null || other == ctx.Target) return OpResult.Continue;

            var transform = ctx.Target.transform;
            var toOther = other.transform.position - transform.position;

            if (BlockyPlane.IsFlat(ctx.Target))
            {
                if (toOther.x * toOther.x + toOther.y * toOther.y > 1e-6f)
                    transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(toOther.y, toOther.x) * Mathf.Rad2Deg);
                return OpResult.Continue;
            }

            toOther.y = 0f;
            if (toOther.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(toOther, Vector3.up);

            return OpResult.Continue;
        }
    }

    /// <summary>
    /// <c>motion.go_to</c> — moves to another object's place. <c>duration = 0</c> is instant; otherwise it closes
    /// the remaining distance each tick, re-reading the target's position, so it follows something that is still
    /// moving instead of gliding to where it used to be.
    /// </summary>
    [BlockExecutor("motion.go_to")]
    public sealed class GoToObjectOp : IBlockOp
    {
        public OpResult Execute(ref OpContext ctx)
        {
            var other = BlockyRuntime.Objects.Find(ctx.GetText(0), ctx.Target);
            if (other == null || other == ctx.Target) return OpResult.Continue;

            var duration = ctx.GetNumber(1);
            var transform = ctx.Target.transform;
            var target = other.transform.position;

            if (duration <= 0f)
            {
                transform.position = target;
                return OpResult.Continue;
            }

            var remaining = duration - ctx.Scratch;
            var step = Mathf.Min(ctx.DeltaTime, remaining);
            transform.position = Vector3.Lerp(transform.position, target, step / remaining);
            ctx.Scratch += step;

            if (ctx.Scratch >= duration)
            {
                transform.position = other.transform.position; // land exactly on it, wherever it ended up
                ctx.Scratch = 0f;
                return OpResult.Continue;
            }

            return OpResult.Retry;
        }
    }
}
