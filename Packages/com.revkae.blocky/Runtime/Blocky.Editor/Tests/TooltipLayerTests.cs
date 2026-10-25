using NUnit.Framework;
using UnityEngine.UIElements;

namespace Blocky.Editor.Tests
{
    /// <summary>
    /// The tooltip a game's panel never draws by itself: when it shows, for which element, and when it goes. Driven
    /// by hand — what the pointer is over, a press, the time — the way the workspace drives it every frame.
    /// </summary>
    public class TooltipLayerTests
    {
        private const float Delay = TooltipLayer.ShowDelay;

        private VisualElement _root;
        private TooltipLayer _layer;
        private Button _go;
        private Label _goLabel;
        private Button _stop;
        private VisualElement _plain;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _go = new Button { tooltip = "Start this object's scripts" };
            _goLabel = new Label("▶");
            _go.Add(_goLabel);
            _stop = new Button { tooltip = "Stop this object's scripts" };
            _plain = new VisualElement();
            _root.Add(_go);
            _root.Add(_stop);
            _root.Add(_plain);

            _layer = new TooltipLayer();
            _root.Add(_layer);
        }

        /// <summary>The pointer arrives on <paramref name="element"/> at <paramref name="time"/> and rests there long enough.</summary>
        private void RestOn(VisualElement element, float time)
        {
            _layer.Tick(element, false, time);
            _layer.Tick(element, false, time + Delay);
        }

        [Test]
        public void ATooltip_ShowsOnlyOnceThePointerHasRestedOnItsElement()
        {
            _layer.Tick(_go, false, 10f);
            _layer.Tick(_go, false, 10f + Delay * 0.9f);
            Assert.IsFalse(_layer.IsShown, "passing over a button is not resting on it");

            _layer.Tick(_go, false, 10f + Delay);
            Assert.IsTrue(_layer.IsShown);
            Assert.AreSame(_go, _layer.ShownFor);
            Assert.AreEqual("Start this object's scripts", _layer.Text);
        }

        [Test]
        public void TheLabelInsideAButton_ShowsTheButtonsTooltip()
        {
            RestOn(_goLabel, 0f);

            Assert.AreSame(_go, _layer.ShownFor);
        }

        [Test]
        public void AnElementWithNoTooltip_ShowsNothing()
        {
            RestOn(_plain, 0f);

            Assert.IsFalse(_layer.IsShown);
            Assert.AreEqual(string.Empty, _layer.Text);
        }

        [Test]
        public void APress_PutsItAway_UntilThePointerHasLeftTheElement()
        {
            RestOn(_go, 0f);
            _layer.Tick(_go, true, 1f);
            Assert.IsFalse(_layer.IsShown, "a click means the learner knows what the button does");

            _layer.Tick(_go, false, 5f);
            Assert.IsFalse(_layer.IsShown, "still on the button it was pressed on");

            _layer.Tick(null, false, 6f);
            RestOn(_go, 7f);
            Assert.IsTrue(_layer.IsShown, "back on it after leaving: a new rest");
        }

        [Test]
        public void MovingStraightOnToTheNextButton_ShowsItsTooltipAtOnce()
        {
            RestOn(_go, 0f);

            _layer.Tick(_stop, false, Delay + 0.05f);

            Assert.AreSame(_stop, _layer.ShownFor, "reading along a bar shouldn't wait at every button");
            Assert.AreEqual("Stop this object's scripts", _layer.Text);
        }

        [Test]
        public void AfterAPause_TheNextTooltipWaitsAgain()
        {
            RestOn(_go, 0f);
            _layer.Tick(null, false, 1f);

            var later = 1f + TooltipLayer.WarmWindow + 0.05f;
            _layer.Tick(_stop, false, later);
            Assert.IsFalse(_layer.IsShown);

            _layer.Tick(_stop, false, later + Delay);
            Assert.AreSame(_stop, _layer.ShownFor);
        }

        [Test]
        public void LeavingForNothing_PutsItAway()
        {
            RestOn(_go, 0f);

            _layer.Tick(null, false, 1f);

            Assert.IsFalse(_layer.IsShown);
            Assert.IsNull(_layer.ShownFor);
        }

        [Test]
        public void ADisabledButton_StillSaysWhatItDoes()
        {
            _go.SetEnabled(false); // Step back, before there is a step to go back to

            RestOn(_go, 0f);

            Assert.AreSame(_go, _layer.ShownFor);
        }

        [Test]
        public void ATooltipThatChangesWhileShown_IsReadAgain()
        {
            RestOn(_go, 0f);

            _go.tooltip = "Carry on from where the scripts were paused"; // Pause became Resume
            _layer.Tick(_go, false, 1f);

            Assert.AreEqual("Carry on from where the scripts were paused", _layer.Text);
        }

        [Test]
        public void AnElementOutsideTheLayersParent_IsNotItsToExplain()
        {
            var gameUi = new VisualElement();
            var gameButton = new Button { tooltip = "The game's own button" };
            gameUi.Add(gameButton);
            gameUi.Add(_root); // the workspace and a game's UI side by side in one panel

            RestOn(gameButton, 0f);

            Assert.IsFalse(_layer.IsShown, "a game's UI shows its own tooltips, or none");
        }

        [Test]
        public void Hide_PutsItAwayAtOnce()
        {
            RestOn(_go, 0f);

            _layer.Hide();

            Assert.IsFalse(_layer.IsShown);
        }
    }
}
