using UnityEngine;

namespace WorldBoxMP
{
    public static class OwnUnit
    {
        private static long _id = -1L;

        private static float _hostHold;

        public static bool Walking { get; private set; }

        public static Actor Main { get; private set; }

        public static void Reset()
        {
            _id = -1L;
            Main = null;
            Walking = false;
            _hostHold = 0f;
        }

        public static bool Owns(long id)
        {
            return id == _id && _hostHold <= 0f;
        }

        public static void Step()
        {
            Actor actor = ControllableUnit.getControllableUnit();
            if (actor == null || !actor.isAlive() || SmoothLoader.isLoading())
            {
                Reset();
                return;
            }
            Main = actor;
            _id = actor.getID();
            float unscaledDeltaTime = Time.unscaledDeltaTime;
            if (_hostHold > 0f)
            {
                _hostHold -= unscaledDeltaTime;
                Walking = false;
                return;
            }
            Vector2 movementVector = ControllableUnit.getMovementVector();
            Walking = movementVector.sqrMagnitude > 1E-06f && !Access.Immovable(actor);
            if (!Walking)
            {
                return;
            }
            float pElapsed = Access.Elapsed(World.world);
            int num = Mathf.Clamp(Config.time_scale_asset?.ticks ?? 1, 1, 8);
            for (int i = 0; i < num; i++)
            {
                Access.PossessedStep(actor, pElapsed, actor.current_position + movementVector);
            }
            ClientSync.Place(actor, actor.current_position, actor.position_height);
            Access.IsMoving(actor) = true;
        }

        public static void OnHostPosition(Actor actor, Vector2 hostPos, bool underForces)
        {
            if (actor == null || actor.getID() != _id)
            {
                return;
            }
            if (underForces)
            {
                _hostHold = 0.2f;
            }
            else if ((hostPos - actor.current_position).sqrMagnitude > 64f)
            {
                ClientSync.Place(actor, hostPos, actor.position_height);
            }
        }
    }
}
