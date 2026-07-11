using System.Collections.Generic;
using F1Game.Core;
using NUnit.Framework;

namespace F1Game.Tests
{
    /// <summary>
    /// The procedural livery generator must produce a visually distinct grid, convert
    /// HSV correctly, and resolve requested-livery collisions/gaps by filling with
    /// distinct placeholders. Pinned here.
    /// </summary>
    public class LiveryGeneratorTests
    {
        [Test]
        public void FromHsvMatchesPrimaryColours()
        {
            LiveryColor red = LiveryGenerator.FromHsv(0f, 1f, 1f);
            Assert.AreEqual(255, red.R);
            Assert.AreEqual(0, red.G);
            Assert.AreEqual(0, red.B);

            LiveryColor green = LiveryGenerator.FromHsv(1f / 3f, 1f, 1f);
            Assert.AreEqual(0, green.R);
            Assert.AreEqual(255, green.G);
            Assert.AreEqual(0, green.B);

            LiveryColor blue = LiveryGenerator.FromHsv(2f / 3f, 1f, 1f);
            Assert.AreEqual(0, blue.R);
            Assert.AreEqual(0, blue.G);
            Assert.AreEqual(255, blue.B);

            // Zero saturation is greyscale.
            LiveryColor grey = LiveryGenerator.FromHsv(0.5f, 0f, 0.5f);
            Assert.AreEqual(grey.R, grey.G);
            Assert.AreEqual(grey.G, grey.B);
        }

        [Test]
        public void GeneratedGridIsPairwiseDistinct()
        {
            const int total = 20;
            var liveries = new List<CarLivery>();
            for (int i = 0; i < total; i++)
            {
                liveries.Add(LiveryGenerator.Generate(i, total));
            }

            for (int i = 0; i < total; i++)
            {
                for (int j = i + 1; j < total; j++)
                {
                    Assert.IsTrue(CarLivery.AreDistinct(liveries[i], liveries[j]),
                        $"cars {i} and {j} look too similar");
                }
            }
        }

        [Test]
        public void AssignDistinctGridKeepsDistinctRequestsAndFillsGaps()
        {
            var requested = new List<CarLivery>
            {
                new CarLivery(new LiveryColor(225, 6, 0), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255)),
                null, // gap -> generated
                new CarLivery(new LiveryColor(0, 114, 198), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255)),
            };

            List<CarLivery> grid = LiveryGenerator.AssignDistinctGrid(requested, 4);
            Assert.AreEqual(4, grid.Count);
            // The two distinct requested primaries are preserved.
            Assert.AreEqual("#E10600", grid[0].Primary.ToHex());
            Assert.AreEqual("#0072C6", grid[2].Primary.ToHex());
            // Every slot is filled and pairwise distinct.
            for (int i = 0; i < grid.Count; i++)
            {
                Assert.IsNotNull(grid[i]);
                for (int j = i + 1; j < grid.Count; j++)
                {
                    Assert.IsTrue(CarLivery.AreDistinct(grid[i], grid[j]));
                }
            }
        }

        [Test]
        public void AssignDistinctGridReplacesCollidingRequests()
        {
            var red = new CarLivery(new LiveryColor(225, 6, 0), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255));
            var nearRed = new CarLivery(new LiveryColor(228, 10, 4), new LiveryColor(0, 0, 0), new LiveryColor(255, 255, 255));
            List<CarLivery> grid = LiveryGenerator.AssignDistinctGrid(new List<CarLivery> { red, nearRed }, 2);
            Assert.AreEqual(2, grid.Count);
            // The second (near-identical) request is replaced so the grid stays readable.
            Assert.IsTrue(CarLivery.AreDistinct(grid[0], grid[1]));
        }
    }
}
