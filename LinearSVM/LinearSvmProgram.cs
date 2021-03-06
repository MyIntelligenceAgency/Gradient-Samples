﻿namespace LinearSVM {
    using System;
    using System.Collections;
    using System.Diagnostics;
    using System.Dynamic;
    using System.Linq;
    using Gradient;
    using Gradient.ManualWrappers;
    using ManyConsole.CommandLineUtils;
    using Python.Runtime;
    using SharPy.Runtime;
    using tensorflow;
    using tensorflow.train;

    class LinearSvmProgram {
        static readonly Random random = new Random();
        static dynamic np;

        readonly LinearSvmCommand flags;

        public LinearSvmProgram(LinearSvmCommand flags)
        {
            this.flags = flags ?? throw new ArgumentNullException(nameof(flags));
        }

        static int Main(string[] args) {
            GradientLog.OutputWriter = Console.Out;

            tf.no_op();

            np = PythonEngine.ImportModule("numpy");
            return ConsoleCommandDispatcher.DispatchCommand(
               ConsoleCommandDispatcher.FindCommandsInSameAssemblyAs(typeof(LinearSvmProgram)),
               args, Console.Out);
        }

        public int Run()
        {
            dynamic datasets = Py.Import("sklearn.datasets");
            dynamic slice = PythonEngine.Eval("slice");
            var iris = datasets.load_iris();
            dynamic firstTwoFeaturesIndex = new PyTuple(new PyObject[] {
                slice(null),
                slice(null, 2)
            });
            var input = iris.data.__getitem__(firstTwoFeaturesIndex);
            IEnumerable target = iris.target;
            var expectedOutput = target.Cast<dynamic>()
                .Select(l => (int)l == 0 ? 1 : -1)
                .ToArray();
            int trainCount = expectedOutput.Length * 4 / 5;
            var trainIn = numpy.np.array(((IEnumerable)input).Cast<dynamic>().Take(trainCount));
            var trainOut = numpy.np.array(expectedOutput.Take(trainCount));
            var testIn = numpy.np.array(((IEnumerable)input).Cast<dynamic>().Skip(trainCount));
            var testOut = numpy.np.array(expectedOutput.Skip(trainCount));

            var inPlace = tf.placeholder(shape: new int?[] { null, input.shape[1] }.Cast<object>(), dtype: tf.float32);
            var outPlace = tf.placeholder(shape: new int?[] { null, 1 }.Cast<object>(), dtype: tf.float32);
            var w = new Variable(tf.random_normal(shape: new TensorShape((int)input.shape[1], 1)));
            var b = new Variable(tf.random_normal(shape: new TensorShape(1, 1)));

            var totalLoss = Loss(w, b, inPlace, outPlace);
            var accuracy = Inference(w, b, inPlace, outPlace);

            var trainOp = new GradientDescentOptimizer(this.flags.InitialLearningRate).minimize(totalLoss);

            var expectedTrainOut = trainOut.reshape((trainOut.Length, 1));
            var expectedTestOut = testOut.reshape((testOut.Length, 1));

            new Session().UseSelf(sess =>
            {
                var init = tensorflow.tf.global_variables_initializer();
                sess.run(init);
                for(int step = 0; step < this.flags.StepCount; step++)
                {
                    (numpy.ndarray @in, numpy.ndarray @out) = NextBatch(trainIn, trainOut, sampleCount: this.flags.BatchSize);
                    var feed = new PythonDict<object, object> {
                        [inPlace] = @in,
                        [outPlace] = @out,
                    };
                    sess.run(trainOp, feed_dict: feed);

                    var loss = sess.run(totalLoss, feed_dict: feed);
                    var trainAcc = sess.run(accuracy, new PythonDict<object, object>
                    {
                        [inPlace] = trainIn,
                        [outPlace] = expectedTrainOut,
                    });
                    var testAcc = sess.run(accuracy, new PythonDict<object, object>
                    {
                        [inPlace] = testIn,
                        [outPlace] = expectedTestOut,
                    });

                    if ((step + 1) % 100 == 0)
                        Console.WriteLine($"Step{step}: test acc {testAcc}, train acc {trainAcc}");
                }

                //if (this.flags.IsEvaluation)
                //{

                //}
            });

            return 0;
        }

        dynamic Loss(dynamic W, dynamic b, dynamic inputData, dynamic targetData) {
            var logits = tf.subtract(tf.matmul(inputData, W), b);
            var normTerm = tf.divide(tf.reduce_sum(tf.multiply(tf.transpose(W), W)), 2);
            var classificationLoss = tf.reduce_mean(tf.maximum(0.0, tf.subtract(this.flags.Delta, tf.multiply(logits, targetData))));
            var totalLoss = tf.add_dyn(tf.multiply(this.flags.C, classificationLoss), tf.multiply(this.flags.Reg, normTerm));
            return totalLoss;
        }

        static dynamic Inference(IGraphNodeBase W, IGraphNodeBase b, dynamic inputData, dynamic targetData) {
            var prediction = tf.sign_dyn(tf.subtract(tf.matmul(inputData, W), b));
            var accuracy = tf.reduce_mean(tf.cast(tf.equal(prediction, targetData), tf.float32));
            return accuracy;
        }

        (numpy.ndarray, numpy.ndarray) NextBatch(dynamic inputData, dynamic targetData, int? sampleCount = null) {
            sampleCount = sampleCount ?? this.flags.BatchSize;
            int max = inputData.Length;
            var indexes = Enumerable.Range(0, sampleCount.Value)
                .Select(_ => random.Next(max))
                .ToArray();

            numpy.ndarray inputBatch = inputData[indexes];
            numpy.ndarray outputBatch = np.reshape(targetData[indexes], (sampleCount.Value, 1));
            if (outputBatch == null)
                throw new InvalidOperationException();
            return (inputBatch, outputBatch);
        }

        class ContextManager {
            public static implicit operator ExitAction(ContextManager _) => new ExitAction();
        }

        public struct ExitAction : IDisposable
        {
            public ExitAction(Action onDispose) {
                this.OnDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
            }
            public Action OnDispose { get; }
            public void Dispose() => this.OnDispose?.Invoke();
        }
    }
}
