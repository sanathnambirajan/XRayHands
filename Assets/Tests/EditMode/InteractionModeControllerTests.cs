using NUnit.Framework;
using UnityEngine;
using XRayHands.Gestures;
using XRayHands.Input;
using XRayHands.Interaction;

namespace XRayHands.Tests
{
    /// <summary>Tool arbitration rules of the interaction controller, with fake tools.</summary>
    public class InteractionModeControllerTests
    {
        private sealed class FakeTool : ITool
        {
            public bool Wants;
            public bool Keep = true;
            public int Begins, Ends;
            public FakeTool(string name, HandUsage usage) { Name = name; Usage = usage; }
            public string Name { get; }
            public HandUsage Usage { get; }
            public bool CanStart(in ToolFrame f) => Wants;
            public void Begin(in ToolFrame f) => Begins++;
            public bool Tick(in ToolFrame f) => Keep;
            public void End() => Ends++;
        }

        private GestureTuning _t;
        private ToolFrame _frame;

        [SetUp]
        public void SetUp()
        {
            _t = GestureTuning.CreateDefault();
            _frame = new ToolFrame(HandFixtures.Context(null, null), new GestureRecognizer(_t), 1f / 72f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_t);

        [Test]
        public void HigherPriorityToolStartsFirst()
        {
            var peel = new FakeTool("Peel", HandUsage.Both) { Wants = true };
            var cut = new FakeTool("Cut", HandUsage.Right) { Wants = true };
            var c = new InteractionModeController(new ITool[] { peel, cut }, null, null);
            c.Tick(_frame);
            Assert.That(c.Active, Is.SameAs(peel));
        }

        [Test]
        public void RunningOneHandTool_IsNotStolenByAnotherOneHandTool()
        {
            var grab = new FakeTool("Grab", HandUsage.Right);
            var cut = new FakeTool("Cut", HandUsage.Right) { Wants = true };
            var c = new InteractionModeController(new ITool[] { grab, cut }, null, null);
            c.Tick(_frame);
            grab.Wants = true;
            c.Tick(_frame);
            Assert.That(c.Active, Is.SameAs(cut), "a running one-hand tool keeps control");
        }

        [Test]
        public void TwoHandGesture_PreemptsRunningOneHandTool()
        {
            var peel = new FakeTool("Peel", HandUsage.Both);
            var cut = new FakeTool("Cut", HandUsage.Right) { Wants = true };
            var c = new InteractionModeController(new ITool[] { peel, cut }, null, null);
            c.Tick(_frame);
            Assert.That(c.Active, Is.SameAs(cut));
            peel.Wants = true;
            c.Tick(_frame);
            Assert.That(c.Active, Is.SameAs(peel));
            Assert.That(cut.Ends, Is.EqualTo(1));
            Assert.That(peel.Begins, Is.EqualTo(1));
        }

        [Test]
        public void ToolEnds_WhenItsTickReturnsFalse()
        {
            var cut = new FakeTool("Cut", HandUsage.Right) { Wants = true };
            var c = new InteractionModeController(new ITool[] { cut }, null, null);
            c.Tick(_frame);
            cut.Wants = false;
            cut.Keep = false;
            c.Tick(_frame);
            Assert.That(c.Active, Is.Null);
            Assert.That(cut.Ends, Is.EqualTo(1));
        }
    }
}
