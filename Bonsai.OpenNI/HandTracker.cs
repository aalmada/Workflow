﻿using OpenCV.Net;
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

        const int bins = 20;

        public override IObservable<Result> Process(IObservable<IplImage> source)
            => Observable.Defer(() =>
            {
                var histogram = new Histogram(1, new[] { bins }, HistogramType.Array, new[] { new float[] { MinDistance, MaxDistance } });

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
                        .GetSubRect(new Rect(binsToIgnore, 0, bins - binsToIgnore, 1));
                    CV.MinMaxLoc(matHistogram,
                         out var _,
                         out var _,
                         out var _,
                         out var maxLocation);

                    // truncate to slighty in front of the user
                    var userDistance = (binsToIgnore + maxLocation.X) * (MaxDistance / (double)bins);
                    var delta = (MaxDistance - MinDistance) / (double)bins;
                    var truncateDistance = userDistance - delta;
                    var truncatedBody = DepthTruncate.Process16U(input, MinDistance, (ushort)truncateDistance);

                    // binarize to be able to find contours
                    var temp = new IplImage(input.Size, IplDepth.U8, 1);
                    var scale = 255.0 / (truncateDistance - MinDistance);
                    CV.ConvertScale(input, temp, scale, -MinDistance * scale); // threshold can't handle 16 bit images
                    var binary = new IplImage(input.Size, IplDepth.U8, 1);
                    CV.Threshold(temp, binary, 0, 255, ThresholdTypes.BinaryInv | ThresholdTypes.Otsu);

                    // find contours on binarized image
                    using (var storage = new MemStorage())
                    {
                        CV.FindContours(binary, storage, out var firstContour, Contour.HeaderSize, ContourRetrieval.External, ContourApproximation.ChainApproxSimple);

                        if (firstContour is null)
                            return Result.Zero;

                        var moments = new Moments(firstContour);

                        // Compute centroid components
                        var x = moments.M10 / moments.M00;
                        var y = moments.M01 / moments.M00;
                        return new Result(1, new Tuple<int, int>((int)x, (int)y));
                    }
                });
            });

        public class Result : IEquatable<Result>
        {
            public static Result Zero = new Result(0, new Tuple<int, int>(0, 0));

            public int Visible { get; }
            public Tuple<int, int> Position { get; }

            public Result(int visible, Tuple<int, int> position)
            {
                Visible = visible;
                Position = position;
            }

            public bool Equals(Result other)
                => Visible.Equals(other.Visible) && Position.Equals(other.Position);

            public override bool Equals(object obj)
                => obj is Result data && Equals(data);
        }
    }
}
