using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Moq;
using NUnit.Framework;
using static System.Linq.Enumerable;

namespace Bud {
  public class TaskGraphTest {
    [Test]
    public void Run_invokes_action() {
      var action = new Mock<Action>();
      new TaskGraph(action.Object, ImmutableArray<TaskGraph>.Empty).Run();
      action.Verify(a => a(), Times.Once);
    }

    [Test]
    public void Run_invokes_actions_of_dependencies_before_dependent() {
      var queue = new ConcurrentQueue<int>();
      var dependency = new TaskGraph(() => queue.Enqueue(1));
      var graph = new TaskGraph(() => queue.Enqueue(2), dependency);
      graph.Run();
      Assert.AreEqual(new[] {1, 2}, queue);
    }

    [Test]
    public void Run_invokes_each_task_only_once() {
      var actionA = new Mock<Action>();
      var taskA = new TaskGraph(actionA.Object, ImmutableArray<TaskGraph>.Empty);
      var taskB = new TaskGraph(() => { }, ImmutableArray.Create(taskA, taskA));
      var taskC = new TaskGraph(() => { }, ImmutableArray.Create(taskB, taskB));
      taskC.Run();
      actionA.Verify(a => a(), Times.Once);
    }

    [Test]
    [Timeout(2000)]
    public void Run_executes_dependencies_in_parallel() {
      int taskCount = 2;
      var countdown = new CountdownEvent(taskCount);
      var tasks = Range(0, taskCount)
        .Select(i => new TaskGraph(() => {
          countdown.Signal();
          countdown.Wait();
        }));
      new TaskGraph(tasks).Run();
    }

    [Test]
    public void ToTaskGraph_creates_correctly_shaped_graph() {
      var deps = ImmutableDictionary<string, IEnumerable<string>>
        .Empty
        .Add("a", new[] {"b", "c"})
        .Add("b", new[] {"d"})
        .Add("c", new[] {"d"})
        .Add("d", Array.Empty<string>());

      var actions = ImmutableDictionary<string, Action>
        .Empty
        .Add("a", new Mock<Action>().Object)
        .Add("b", new Mock<Action>().Object)
        .Add("c", new Mock<Action>().Object)
        .Add("d", new Mock<Action>().Object);

      var taskNames = new Mock<Func<string, string>>();
      foreach (var task in deps.Keys) {
        taskNames.Setup(s => s(task)).Returns(task);
      }

      var taskGraph = TaskGraph.ToTaskGraph(new[] {"a", "b"}, taskNames.Object, s => deps[s], s => actions[s]);

      var a = taskGraph.Dependencies[0];
      var b = taskGraph.Dependencies[1];
      var c = a.Dependencies[1];
      var d = b.Dependencies[0];

      Assert.AreEqual(new[] {a, b}, taskGraph.Dependencies);
      Assert.AreEqual(new[] {b, c}, a.Dependencies);
      Assert.AreEqual(new[] {d}, b.Dependencies);
      Assert.AreEqual(new[] {d}, c.Dependencies);
      Assert.IsEmpty(d.Dependencies);

      Assert.IsNull(taskGraph.Action);
      Assert.AreSame(actions["a"], a.Action);
      Assert.AreSame(actions["b"], b.Action);
      Assert.AreSame(actions["c"], c.Action);
      Assert.AreSame(actions["d"], d.Action);

      foreach (var task in deps.Keys) {
        taskNames.Verify(s => s(task), Times.AtMostOnce);
      }
    }

    [Test]
    public void ToTaskGraph_throws_when_cycles_present() {
      var deps = ImmutableDictionary<string, IEnumerable<string>>.Empty
                                                                 .Add("a", new[] {"b"})
                                                                 .Add("b", new[] {"c"})
                                                                 .Add("c", new[] {"a"});
      var taskNames = new Mock<Func<string, string>>();
      foreach (var task in deps.Keys) {
        taskNames.Setup(s => s(task)).Returns(task);
      }

      var ex = Assert.Throws<Exception>(() => TaskGraph.ToTaskGraph(new[] {"a"}, s => s, s => deps[s], s => null));

      Assert.That(ex.Message,
                  Does.Contain("a depends on b depends on c depends on a"));

      foreach (var task in deps.Keys) {
        taskNames.Verify(s => s(task), Times.AtMostOnce);
      }
    }

    [Test]
    public void ToTaskGraph_from_actions() {
      Action a = () => Console.WriteLine("Foo");
      Action b = () => Console.WriteLine("Bar");
      var taskGraph = TaskGraph.ToTaskGraph(a, b);
      Assert.AreEqual(null, taskGraph.Action);
      Assert.AreEqual(a, taskGraph.Dependencies[0].Action);
      Assert.AreEqual(b, taskGraph.Dependencies[1].Action);
    }

    [Test]
    public void ToTaskGraph_detects_name_clashes() {
      var taskNames = new Mock<Func<int, string>>();
      taskNames.Setup(s => s(0)).Returns("foo");
      taskNames.Setup(s => s(1)).Returns("foo");

      var exception = Assert.Throws<Exception>(() => TaskGraph.ToTaskGraph(new[] {0, 1}, taskNames.Object,
                                                                           s => Empty<int>(),
                                                                           s => () => { }));
      Assert.AreEqual(exception.Message,
                      "Detected multiple tasks with the name 'foo'. Tasks must have unique names.");
      taskNames.Verify(s => s(0), Times.AtMostOnce);
      taskNames.Verify(s => s(1), Times.AtMostOnce);
    }
  }
}