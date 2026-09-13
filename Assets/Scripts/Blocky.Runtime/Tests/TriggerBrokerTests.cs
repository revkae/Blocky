using Blocky.Runtime.Triggers;
using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class TriggerBrokerTests
    {
        [Test]
        public void FirePlayClicked_FiresOnlyOnce_UntilReset()
        {
            var broker = new TriggerBroker();
            var count = 0;
            broker.OnPlayClicked += () => count++;

            broker.FirePlayClicked();
            broker.FirePlayClicked();
            Assert.AreEqual(1, count);

            broker.ResetPlaySession();
            broker.FirePlayClicked();
            Assert.AreEqual(2, count);
        }

        [Test]
        public void RaiseCollided_ForwardsSourceToListeners()
        {
            var broker = new TriggerBroker();
            GameObject received = null;
            broker.OnCollided += (source, _) => received = source;

            var go = new GameObject("source");
            try
            {
                broker.RaiseCollided(go, null);
                Assert.AreEqual(go, received);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PollLookedAt_FiresOnRisingEdgeOnly()
        {
            var broker = new TriggerBroker();
            var camera = new GameObject("cam").AddComponent<Camera>();
            var target = new GameObject("target");
            target.transform.position = camera.transform.position + camera.transform.forward * 10f;

            try
            {
                var fireCount = 0;
                broker.RegisterLookedAt(target, 15f);
                broker.OnLookedAt += t => { if (t == target) fireCount++; };

                broker.PollLookedAt(camera);
                broker.PollLookedAt(camera);

                Assert.AreEqual(1, fireCount);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void PollLookedAt_DestroyedTarget_IsRemovedWithoutThrowing()
        {
            var broker = new TriggerBroker();
            var camera = new GameObject("cam").AddComponent<Camera>();
            var target = new GameObject("target");
            broker.RegisterLookedAt(target, 15f);

            Object.DestroyImmediate(target);

            Assert.DoesNotThrow(() => broker.PollLookedAt(camera));
            Object.DestroyImmediate(camera.gameObject);
        }
    }
}
