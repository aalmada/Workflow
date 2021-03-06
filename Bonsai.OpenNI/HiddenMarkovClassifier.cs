﻿using Accord.Math;
using Accord.Statistics.Distributions.Fitting;
using Accord.Statistics.Distributions.Multivariate;
using Accord.Statistics.Models.Markov;
using Accord.Statistics.Models.Markov.Learning;
using Accord.Statistics.Models.Markov.Topology;
using Gestures.HMMs;
using NetFabric;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Security.Cryptography;

namespace Bonsai.OpenNI
{
    public class HiddenMarkovClassifier: Transform<HandTracker.Result, string>
    {
        [Description("The name of the database file.")]
        [FileNameFilter("XML Files (*.xml)|*.xml|All Files|*.*")]
        [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)] 
        public string Database { get; set; }

        [Description("The minimum total length in pixels for the gesture.")]
        public int MinGestureLength { get; set; } = 15;

        [Description("The maximum distance in pixels between two points in the gesture.")]
        public int MaxGesturesSpeed { get; set; } = 20;

        public override IObservable<string> Process(IObservable<HandTracker.Result> source)
             => Observable.Defer(() =>
             {
                 var database = new Database();

                 using (var stream = File.OpenRead(Database))
                 {
                     database.Load(stream);
                 }

                 var samples = database.Samples;
                 var classes = database.Classes;

                 var inputs = new double[samples.Count][][];
                 var outputs = new int[samples.Count];

                 for (var i = 0; i < inputs.Length; i++)
                 {
                     inputs[i] = samples[i].Input;
                     outputs[i] = samples[i].Output;
                 }

                 var states = 5;
                 var iterations = 0;
                 var tolerance = 0.01;
                 var rejection = false;

                 var hmm = new HiddenMarkovClassifier<MultivariateNormalDistribution, double[]>(
                     classes.Count,
                     new Forward(states), 
                     new MultivariateNormalDistribution(2), classes.ToArray());

                 // Create the learning algorithm for the ensemble classifier
                 var teacher = new HiddenMarkovClassifierLearning<MultivariateNormalDistribution, double[]>(hmm)
                 {
                     // Train each model using the selected convergence criteria
                     Learner = i => new BaumWelchLearning<MultivariateNormalDistribution, double[]>(hmm.Models[i])
                     {
                         Tolerance = tolerance,
                         MaxIterations = iterations,

                         FittingOptions = new NormalOptions
                         {
                             Regularization = 1e-5
                         }
                     }
                 };

                 teacher.Empirical = true;
                 teacher.Rejection = rejection;

                 // Run the learning algorithm
                 _ = teacher.Learn(inputs, outputs);

                 // run the detection
                 var triggers = source.Select(result => result.Visible).DistinctUntilChanged();
                 var start = triggers.Where(visible => visible != 0);
                 var end = triggers.Where(visible => visible == 0);

                 return source
                    .Window(start, _ => end)
                    .Select(window => window.Select(result => result.Position).ToArray()
                        .Where(points =>
                        {
                            if (points.Length < 2)
                                return false;

                            var length = 0.0;
                            for (var index = 1; index < points.Length; index++)
                            {
                                var distance = Distance(points[index - 1], points[index]);
                                if (distance < MaxGesturesSpeed)
                                    length += distance;
                            }
                            return length > MinGestureLength;
                        })
                        .Select(points =>
                        {
                            var index = hmm.Decide(Preprocess(points));
                            return database.Classes[index];
                        })).Switch();
             });

        static double[][] Preprocess(Tuple<int, int>[] sequence)
        {
            var result = new double[sequence.Length][];
            for (var index = 0; index < sequence.Length; index++)
            {
                result[index] = new double[] { sequence[index].Item1, sequence[index].Item2 };
            }

            var zscores = Accord.Statistics.Tools.ZScores(result);

            return zscores.Add(10);
        }

        static double Distance(Tuple<int, int> from, Tuple<int, int> to)
            => Math.Sqrt(Math.Pow(to.Item1 - from.Item1, 2.0) + Math.Pow(to.Item2 - from.Item2, 2.0));
    }
}
