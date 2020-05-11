using OpenCV.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using System.Runtime.InteropServices;

namespace Bonsai.OpenNI
{
    public class HandTracker : Transform<IplImage, HandTracker.Result>
    {
        [Range(0, 4000)]
        [Precision(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The minimum hand distance.")]
        [DefaultValue(500)]
        public ushort MinDistance { get; set; } = 500;

        [Range(0, 4000)]
        [Precision(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The maximum hand distance.")]
        [DefaultValue(4000)]
        public ushort MaxDistance { get; set; } = 4000;

        [Range(0, 255)]
        [Precision(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The minimum area of the blob.")]
        [DefaultValue(100)]
        public ushort MinArea { get; set; } = 10;

        const int Bins = 20;

        public override IObservable<Result> Process(IObservable<IplImage> source)
            => Observable.Defer(() =>
            {
                var histogram = new Histogram(1, new[] { Bins }, HistogramType.Array, new[] { new float[] { MinDistance, MaxDistance } });

                return source.Select(input =>
                {
                    // truncate depth to ignore unwanted depths
                    var truncatedInput = DepthTruncate.Process16U(input, MinDistance, MaxDistance);

                    // calculate the histogram 
                    histogram.CalcArrHist(new[] { truncatedInput }, true);
                    histogram.Normalize(1);

                    // get the location of the maximum value
                    const int binsToIgnore = 1;
                    var matHistogram = histogram.Bins
                        .GetMat(true)
                        .Reshape(0, 1)
                        .GetSubRect(new Rect(binsToIgnore, 0, Bins - binsToIgnore, 1));
                    CV.MinMaxLoc(matHistogram,
                         out var _,
                         out var _,
                         out var _,
                         out var maxLocation);

                    // truncate to slighty in front of the user
                    var userDistance = (binsToIgnore + maxLocation.X) * (MaxDistance / (double)Bins);
                    var delta = (MaxDistance - MinDistance) / (double)Bins;
                    var truncateDistance = userDistance - delta;
                    var truncatedBody = DepthTruncate.Process16U(input, MinDistance, (ushort)truncateDistance);

                    // binarize to be able to find contours
                    var temp = new IplImage(input.Size, IplDepth.U8, 1);
                    var scale = 255.0 / (truncateDistance - MinDistance);
                    CV.ConvertScale(input, temp, scale, -MinDistance * scale); // threshold can't handle 16 bit images
                    var binary = new IplImage(input.Size, IplDepth.U8, 1);
                    _ = CV.Threshold(temp, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

                    // find contours on binarized image
                    using var storage = new MemStorage();
                    _ = CV.FindContours(binary, storage, out var firstContour, Contour.HeaderSize, ContourRetrieval.External, ContourApproximation.ChainApproxSimple);

                    // get the largest contour
                    Seq largestCountour = null;
                    //Moments largestMoments = default;
                    var largestArea = 0.0;
                    for (var contour = firstContour; contour is object; contour = contour.HNext)
                    {
                        //var moments = new Moments(contour);
                        //var area = moments.M00;

                        var box = CV.BoundingRect(contour);
                        var area = box.Width * box.Height;

                        if (area > MinArea && area > largestArea)
                        {
                            largestArea = area;
                            largestCountour = contour;
                            //largestMoments = moments;
                        }
                    }

                    var contoursImages = new IplImage(input.Size, IplDepth.U8, 1);
                    CV.SetIdentity(contoursImages, Scalar.All(0));

                    if (largestCountour is null)
                        return new Result(0, new Tuple<int, int>(0, 0), contoursImages);

                    // draw largest contour
                    CV.DrawContours(contoursImages, largestCountour, Scalar.All(255), Scalar.All(0), 0);

                    // Compute centroid components
                    var moments = new Moments(largestCountour);
                    var x = moments.M10 / moments.M00;
                    var y = moments.M01 / moments.M00;

                    return new Result(1, new Tuple<int, int>((int)x, (int)y), contoursImages);
                });
            });

        public class Result : IEquatable<Result>
        {
            public static Result Zero = new Result(0, new Tuple<int, int>(0, 0), null);

            public int Visible { get; }
            public Tuple<int, int> Position { get; }
            public IplImage Image { get; }

            public Result(int visible, Tuple<int, int> position, IplImage image)
            {
                Visible = visible;
                Position = position;
                Image = image;
            }

            public bool Equals(Result other)
                => Visible.Equals(other.Visible) && Position.Equals(other.Position);

            public override bool Equals(object obj)
                => obj is Result data && Equals(data);
        }
    }
}
