using F1Game.Track;
using NUnit.Framework;
using UnityEngine;

namespace F1Game.Tests
{
    public class TrackDefinitionTests
    {
        static TrackDefinitionAsset MakeValid()
        {
            var asset = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            for (int i = 0; i < 32; i++)
            {
                float angle = i / 32f * Mathf.PI * 2f;
                asset.spline.Add(new TrackDefinitionAsset.SplinePoint
                {
                    position = new Vector3(Mathf.Cos(angle) * 500f, 0f, Mathf.Sin(angle) * 500f),
                    width = 12f,
                });
            }

            for (int i = 0; i < 22; i++)
            {
                asset.gridSlots.Add(new TrackDefinitionAsset.GridSlot());
            }

            asset.sectorBoundaryDistances = new float[] { 1000f, 2100f };
            return asset;
        }

        [Test]
        public void ValidAssetPassesValidation()
        {
            Assert.IsEmpty(MakeValid().Validate());
        }

        [Test]
        public void ValidationCatchesAuthoringMistakes()
        {
            var tooFewPoints = ScriptableObject.CreateInstance<TrackDefinitionAsset>();
            Assert.IsNotEmpty(tooFewPoints.Validate());

            var narrow = MakeValid();
            var point = narrow.spline[3];
            point.width = 5f;
            narrow.spline[3] = point;
            Assert.IsTrue(narrow.Validate().Exists(problem => problem.Contains("width")));

            var shortGrid = MakeValid();
            shortGrid.gridSlots.RemoveRange(0, 4);
            Assert.IsTrue(shortGrid.Validate().Exists(problem => problem.Contains("grid")));

            var badLine = MakeValid();
            badLine.racingLineOffsets.Add(0.5f);
            Assert.IsTrue(badLine.Validate().Exists(problem => problem.Contains("Racing line")));
        }

        [Test]
        public void ComputedLengthMatchesCircleCircumference()
        {
            // 32-point ring of radius 500 -> polygon perimeter slightly under 2*pi*r.
            float length = MakeValid().ComputeLength();
            Assert.AreEqual(2f * Mathf.PI * 500f, length, 25f);
        }
    }
}
