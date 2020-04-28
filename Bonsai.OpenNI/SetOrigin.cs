using OpenCV.Net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bonsai.OpenNI
{
    public class SetOrigin : Transform<Point2f, SetOrigin.Data>
    {
        public System.Drawing.Size Size { get; set; }

        public override IObservable<Data> Process(IObservable<Point2f> source)
        {
            //var origin = source
            //    .Scan(
            //        new Point2f(float.NaN, float.NaN),
            //        (previous, current) =>
            //        {
            //            if (IsNaN(current) || IsNaN(previous))
            //                return current;

            //            return previous;
            //        })
            //        .Select(origin =>
            //            {
            //                if (IsNaN(origin))
            //                    return Data.Zero;

            //                return new Data(1, new Point((int)origin.X, (int)origin.Y));
            //            })
            //            .DistinctUntilChanged();

            //return Observable
            //    .CombineLatest(
            //        source,
            //        origin,
            //        (current, origin) =>
            //        {
            //            if (origin.Visible == 0 || IsNaN(current))
            //                return Data.Zero;

            //            return new Data(1, new Point((int)current.X - origin.Position.X, (int)current.Y - origin.Position.Y));
            //        })
            //        .StartWith(Data.Zero);

            return source
                .Select(
                    current =>
                    {
                        if (IsNaN(current))
                            return Data.Zero;

                        return new Data(1, new Point((int)current.X, (int)current.Y));
                    })
                    .StartWith(Data.Zero);
        }

        static bool IsNaN(Point2f point)
            => float.IsNaN(point.X); // || float.IsNaN(point.Y);

        public class Data : IEquatable<Data>
        {
            public static readonly Data Zero = new Data(0, Point.Zero);

            public int Visible { get; }
            public Point Position { get; }

            public Data(int visible, Point position)
            {
                Visible = visible;
                Position = position;
            }

            public bool Equals(Data other)
                => Visible.Equals(other.Visible) && Position.Equals(other.Position);

            public override bool Equals(object obj)
                => obj is Data data && Equals(data);
        }
    }
}
