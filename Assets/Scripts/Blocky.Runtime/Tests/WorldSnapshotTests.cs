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
    }
}
