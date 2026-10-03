using NUnit.Framework;
using UnityEngine;

namespace Blocky.Runtime.Tests
{
    public class WorldSnapshotTests
    {
        private GameObject _cube;
        private Material _copy;

        [SetUp]
        public void SetUp()
        {
            _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_cube);
            if (_copy != null) Object.DestroyImmediate(_copy);
        }

        [Test]
        public void RestoreAll_PutsTransformVisibilityAndMaterialBack()
        {
            var renderer = _cube.GetComponent<Renderer>();
            var original = renderer.sharedMaterial;
            _cube.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 30f, 0f));
            _cube.transform.localScale = new Vector3(1f, 2f, 1f);

            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            _cube.transform.SetPositionAndRotation(new Vector3(9f, 9f, 9f), Quaternion.Euler(45f, 0f, 90f));
            _cube.transform.localScale = Vector3.one * 3f;
            renderer.enabled = false;
            _copy = new Material(original);
            renderer.sharedMaterial = _copy; // what "change color" leaves behind

            snapshot.RestoreAll();

            Assert.AreEqual(new Vector3(1f, 2f, 3f), _cube.transform.position);
            Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 30f, 0f), _cube.transform.rotation), 1e-3f);
            Assert.AreEqual(new Vector3(1f, 2f, 1f), _cube.transform.localScale);
            Assert.IsTrue(renderer.enabled);
            Assert.AreSame(original, renderer.sharedMaterial);
        }

        [Test]
        public void RestoreAll_ShowsEveryRendererUnderTheObjectAgain()
        {
            // "set visible" hides every renderer under the object, so Reset has to put every one back.
            var child = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            child.transform.SetParent(_cube.transform, false);
            var hiddenFromTheStart = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            hiddenFromTheStart.transform.SetParent(_cube.transform, false);
            hiddenFromTheStart.GetComponent<Renderer>().enabled = false;

            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            foreach (var renderer in _cube.GetComponentsInChildren<Renderer>()) renderer.enabled = false;

            snapshot.RestoreAll();

            Assert.IsTrue(_cube.GetComponent<Renderer>().enabled);
            Assert.IsTrue(child.GetComponent<Renderer>().enabled, "a child mesh comes back too");
            Assert.IsFalse(hiddenFromTheStart.GetComponent<Renderer>().enabled, "a part that started hidden stays hidden");
        }

        [Test]
        public void Capture_KeepsTheFirstState()
        {
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            _cube.transform.position = new Vector3(5f, 0f, 0f);
            snapshot.Capture(_cube); // a hot-reload re-initializes the runner: must not move the starting point
            _cube.transform.position = new Vector3(7f, 0f, 0f);

            snapshot.RestoreAll();

            Assert.AreEqual(Vector3.zero, _cube.transform.position);
        }

        [Test]
        public void RestoreAll_StopsAMovingBody()
        {
            var body = _cube.AddComponent<Rigidbody>();
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            body.linearVelocity = new Vector3(3f, 0f, 0f);
            body.angularVelocity = new Vector3(0f, 2f, 0f);
            snapshot.RestoreAll();

            Assert.AreEqual(Vector3.zero, body.linearVelocity);
            Assert.AreEqual(Vector3.zero, body.angularVelocity);
        }

        [Test]
        public void RestoreMoment_PutsBackThatMoment_AndRecolorsOnlyACopiedMaterial()
        {
            var renderer = _cube.GetComponent<Renderer>();
            var original = renderer.sharedMaterial;
            var originalColor = original.color;
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            _copy = new Material(original) { color = Color.red }; // what "change color" leaves behind
            renderer.sharedMaterial = _copy;
            _cube.transform.position = new Vector3(1f, 0f, 0f);
            var moment = snapshot.SaveMoment();

            _cube.transform.position = new Vector3(5f, 0f, 0f);
            _copy.color = Color.blue;
            snapshot.RestoreMoment(moment);

            Assert.AreEqual(new Vector3(1f, 0f, 0f), _cube.transform.position);
            Assert.AreSame(_copy, renderer.sharedMaterial);
            Assert.AreEqual(Color.red, _copy.color);

            snapshot.RestoreAll();
            Assert.AreSame(original, renderer.sharedMaterial);
            Assert.AreEqual(originalColor, original.color); // the shared original is never written to
        }

        [Test]
        public void RestoreAll_ForgetsDestroyedObjects()
        {
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);
            Object.DestroyImmediate(_cube);

            Assert.DoesNotThrow(snapshot.RestoreAll);
            Assert.AreEqual(0, snapshot.Count);
        }

        // ---- a game that lets its objects be placed between runs (ADR-043) ---------------------------------

        [Test]
        public void Recapture_MakesWhereTheObjectIsNow_WhatResetReturnsItTo()
        {
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            _cube.transform.position = new Vector3(5f, 0f, 0f); // the learner dragged it
            snapshot.Recapture(_cube);
            _cube.transform.position = new Vector3(9f, 0f, 0f); // a script moved it
            snapshot.RestoreAll();

            Assert.AreEqual(new Vector3(5f, 0f, 0f), _cube.transform.position);
            Assert.AreEqual(1, snapshot.Count, "still one object, not two");
        }

        [Test]
        public void Recapture_OfAnObjectNeverSeen_StartsTrackingIt()
        {
            var snapshot = new WorldSnapshot();
            _cube.transform.position = new Vector3(2f, 0f, 0f);

            snapshot.Recapture(_cube);
            _cube.transform.position = Vector3.zero;
            snapshot.RestoreAll();

            Assert.AreEqual(new Vector3(2f, 0f, 0f), _cube.transform.position);
        }

        [Test]
        public void RecaptureAll_MovesEveryStart_AndForgetsDestroyedObjects()
        {
            var other = new GameObject("WorldSnapshotOther");
            var gone = new GameObject("WorldSnapshotGone");
            try
            {
                var snapshot = new WorldSnapshot();
                snapshot.Capture(_cube);
                snapshot.Capture(other);
                snapshot.Capture(gone);

                _cube.transform.position = new Vector3(1f, 0f, 0f);
                other.transform.position = new Vector3(0f, 2f, 0f);
                Object.DestroyImmediate(gone);
                snapshot.RecaptureAll();

                _cube.transform.position = Vector3.zero;
                other.transform.position = Vector3.zero;
                snapshot.RestoreAll();

                Assert.AreEqual(new Vector3(1f, 0f, 0f), _cube.transform.position);
                Assert.AreEqual(new Vector3(0f, 2f, 0f), other.transform.position);
                Assert.AreEqual(2, snapshot.Count);
            }
            finally
            {
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Forget_StopsTrackingTheObject_SoResetLeavesItAlone()
        {
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            snapshot.Forget(_cube);
            _cube.transform.position = new Vector3(4f, 0f, 0f);
            snapshot.RestoreAll();

            Assert.AreEqual(new Vector3(4f, 0f, 0f), _cube.transform.position);
            Assert.AreEqual(0, snapshot.Count);
            Assert.DoesNotThrow(() => snapshot.Forget(null));
        }

        [Test]
        public void AfterRecapture_ResetRecolorsACopy_ButNeverTheMaterialTheObjectFirstHad()
        {
            var renderer = _cube.GetComponent<Renderer>();
            var original = renderer.sharedMaterial; // in the Editor, the project's own asset
            var originalColor = original.color;
            var snapshot = new WorldSnapshot();
            snapshot.Capture(_cube);

            // "change color" left a red copy on the object, and then the learner placed it: the copy is its start now.
            _copy = new Material(original) { color = Color.red };
            renderer.sharedMaterial = _copy;
            snapshot.Recapture(_cube);

            _copy.color = Color.blue; // the next run recolors the copy
            snapshot.RestoreAll();
            Assert.AreSame(_copy, renderer.sharedMaterial);
            Assert.AreEqual(Color.red, _copy.color, "the copy goes back to how it was when the object was placed");
            Assert.AreEqual(originalColor, original.color);

            // And an object back on its original material at a Recapture never has that material written to.
            renderer.sharedMaterial = original;
            snapshot.Recapture(_cube);
            renderer.sharedMaterial = _copy;
            snapshot.RestoreAll();
            Assert.AreSame(original, renderer.sharedMaterial);
            Assert.AreEqual(originalColor, original.color, "the shared original is never written to");
        }

        [Test]
        public void Reset_CoversEveryRendererUnderTheObject_NotOnlyItsOwn()
        {
            // An imported model: a root with no renderer of its own, and its meshes under it.
            var root = new GameObject("WorldSnapshotModel");
            var part = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            part.transform.SetParent(root.transform, false);
            try
            {
                var renderer = part.GetComponent<Renderer>();
                var original = renderer.sharedMaterial;
                var snapshot = new WorldSnapshot();
                snapshot.Capture(root);

                _copy = new Material(original) { color = Color.green }; // what "change color" leaves on a child
                renderer.sharedMaterial = _copy;
                var moment = snapshot.SaveMoment();
                _copy.color = Color.black;

                snapshot.RestoreMoment(moment);
                Assert.AreEqual(Color.green, _copy.color, "Step back puts a child's color back");

                snapshot.RestoreAll();
                Assert.AreSame(original, renderer.sharedMaterial, "Reset puts a child's material back");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ResettingOneObject_AlsoPutsBackTheObjectsItsScriptsActOn()
        {
            var driven = new GameObject("WorldSnapshotDriven");
            var stranger = new GameObject("WorldSnapshotStranger");
            try
            {
                var snapshot = new WorldSnapshot();
                snapshot.Capture(_cube);
                snapshot.Capture(stranger);
                snapshot.CaptureDriven(_cube, driven); // an "as [driven]" block in the cube's script

                driven.transform.position = new Vector3(0f, 0f, 3f);
                stranger.transform.position = new Vector3(0f, 0f, 3f);
                var moment = snapshot.SaveMoment(_cube);
                driven.transform.position = new Vector3(0f, 0f, 7f);

                snapshot.RestoreMoment(moment);
                Assert.AreEqual(3f, driven.transform.position.z, 1e-4f, "a step of the cube covers what it drives");

                snapshot.RestoreObject(_cube);
                Assert.AreEqual(0f, driven.transform.position.z, 1e-4f);
                Assert.AreEqual(3f, stranger.transform.position.z, 1e-4f, "an object nothing of the cube's touched stays where it is");
            }
            finally
            {
                Object.DestroyImmediate(driven);
                Object.DestroyImmediate(stranger);
            }
        }
    }
}
