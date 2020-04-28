using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Schema;

namespace Bonsai.OpenNI
{
    [Description("Applies a near and far truncate to the depth map.")]
    public class DepthTruncate : Transform<IplImage, IplImage>
    {
        const int MaxThresholdValue = 4000; // 4 meters when using PixelFormat.Depth1Mm
        const int DefaultLowThreshold = 0;
        const int DefaultHighThreshold = MaxThresholdValue;
        const bool DefaultBinary = false;
        const bool DefaultOutput8Bit = false;

        [Range(0, MaxThresholdValue)]
        [Precision(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The threshold value used to crop lower than values.")]
        [DefaultValue(DefaultLowThreshold)]
        public ushort LowThreshold { get; set; } = DefaultLowThreshold;

        [Range(0, MaxThresholdValue)]
        [Precision(0, 1)]
        [Editor(DesignTypes.SliderEditor, DesignTypes.UITypeEditor)]
        [Description("The threshold value used to crop greater than values.")]
        [DefaultValue(DefaultHighThreshold)]
        public ushort HighThreshold { get; set; } = DefaultHighThreshold;

        [Description("Binarizes the resulting depth map when on 8 bit mode.")]
        [DefaultValue(DefaultBinary)]
        public bool Binary { get; set; } = DefaultBinary;

        [Description("Converts the resulting depth map to an 8 bit depth map.")]
        [DefaultValue(DefaultOutput8Bit)]
        public bool Output8Bit { get; set; } = DefaultOutput8Bit;

        public override IObservable<IplImage> Process(IObservable<IplImage> source)
            => source.SelectMany(input =>
            {
                if (input.Depth != IplDepth.U16)
                    return Observable.Throw<IplImage>(new Exception($"{nameof(DepthTruncate)} can only handle 16 bit single channel depth maps."));

                if (Output8Bit)
                    return Observable.Return(Process8U(input));

                return Observable.Return(Process16U(input, LowThreshold, HighThreshold, Binary));
            });

        IplImage Process8U(IplImage input)
        {
            var output = new IplImage(input.Size, IplDepth.U8, 1);

            if (LowThreshold >= HighThreshold)
            {
                output.GetMat().SetZero();
            }
            else if(Binary)
            {
                Transform<ushort, byte>(input, output,
                    value =>
                    {
                        if (value < LowThreshold || value > HighThreshold)
                            return byte.MinValue;

                        return byte.MaxValue;
                    });
            }
            else
            {
                var scale = (double)byte.MaxValue / (HighThreshold - LowThreshold);
                Transform<ushort, byte>(input, output,
                    value =>
                    {
                        if (value < LowThreshold)
                            return byte.MinValue;
                        if (value < HighThreshold)
                            return (byte)(scale * (value - LowThreshold));
                        return byte.MaxValue;
                    });
            }

            return output;
        }

        public static IplImage Process16U(IplImage input, ushort lowThreshold, ushort highThreshold, bool binary = false)
        {
            var output = new IplImage(input.Size, IplDepth.U16, 1);

            if (lowThreshold >= highThreshold)
            {
                output.GetMat().SetZero();
            }
            else if (binary)
            {
                Transform<ushort, ushort>(input, output,
                    value =>
                    {
                        if (value < lowThreshold || value > highThreshold)
                            return ushort.MinValue;

                        return ushort.MaxValue;
                    });
            }
            else
            {
                Transform<ushort, ushort>(input, output,
                    value =>
                    {
                        if (value < lowThreshold)
                            return lowThreshold;
                        if (value < highThreshold)
                            return value;
                        return highThreshold;
                    });
            }

            return output;
        }

        static unsafe void Transform<TInput, TOutput>(IplImage input, IplImage output, Func<TInput, TOutput> func)
        {
            var rows = input.Size.Height;
            var columns = input.WidthStep / Unsafe.SizeOf<TInput>();

            for (var row = 0; row < rows; row++)
            {
                ref var inputRow = ref Unsafe.AsRef<TInput>(input.GetRow(row).Data.ToPointer());
                ref var outputRow = ref Unsafe.AsRef<TOutput>(output.GetRow(row).Data.ToPointer());
                for (var column = 0; column < columns; column++)
                {
                    ref var inputValue = ref Unsafe.Add(ref inputRow, column);
                    ref var outputValue = ref Unsafe.Add(ref outputRow, column);
                    outputValue = func(inputValue);
                }
            }
        }
    }
}
