using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Syntax;
using static System.Linq.Enumerable;

namespace Syntax {
    #region Helpers
    public static class RtfToString {
        /// <summary>
        /// Convert
        /// </summary>
        /// <param name="richText"></param>
        /// <returns></returns>
        public static string Convert(string richText) {
            using (var box = new RichTextBox()) {
                try {
                    box.Rtf = richText;
                } catch (Exception e) {
                    if (e is ArgumentException) return richText;
                    throw;
                }
                return box.Text;
            }
        }
    }

    public static class EnumerableExtensions {
        /// <summary>
        /// Return a new IEnumerable&lt;T&gt; with elements appended.   
        /// </summary>
        public static IEnumerable<T> Append<T>(this IEnumerable<T> source, params T[] tail)
            => source.Concat(tail);

        /// <summary>
        /// Return a new IEnumerable&lt;T&gt; with elements prepended.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<T> Prepend<T>(this IEnumerable<T> source, params T[] head)
            => head.Concat(source);

        /// <summary>
        /// Equivalent for string.Join.
        /// </summary>
        public static string Join<T>(this IEnumerable<T> source, string separator)
            => string.Join(separator, source);

        /// <summary>
        /// Join strings without seperator.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static string Join<T>(this IEnumerable<T> source)
            => source.Join(string.Empty);

        /// <summary>
        /// Split and join
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <param name="separator"></param>
        /// <returns></returns>
        public static IEnumerable<string> JoinSplit<T>(this IEnumerable<T> source, char separator)
            => source
                .Join()
                .Split(separator)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s) && !string.IsNullOrWhiteSpace(s));
    }
    #endregion

    public class Tree : IEnumerable<Tree>, IEnumerable {
        public static int Offset { private get; set; } 
            = 35;

        #region regex
        private static Regex RegexIf { get; }
            = new Regex(@"^{\s*if\s+.*?}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static Regex RegexEndif { get; }
            = new Regex(@"^{\s*end\s+if\s*?}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static Regex RegexAsk { get; }
            = new Regex(@"^{.*?ask\s*\(.*?\).*?}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static Regex RegexInput { get; }
            = new Regex(@"^{\s*input\s+.*?}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public bool IsIf => RegexIf.IsMatch(ToString());

        public bool IsEndIf => RegexEndif.IsMatch(ToString());

        public bool IsAsk => RegexAsk.IsMatch(ToString());

        public bool IsInput => RegexInput.IsMatch(ToString());
        #endregion

        public static Tree Create(string text) {
            var stack = new Stack<Tree>();
            var root = new Tree { Parent = null, Head = -1, Tail = text.Length, Text = text };
            stack.Push(root);
            for (var i = 0; i != text.Length; ++i) {
                if (text[i] == '{') {
                    var tree = new Tree { Parent = stack.Peek(), Head = i, Text = text };
                    stack.Peek().Children.Add(tree);
                    stack.Push(tree);
                }
                if (text[i] == '}') {
                    if (stack.Count < 2) {
                        root
                            .Warnings
                            .AddRange(Complain(text, i, i, Offset).Prepend("Extra '}' found as following:"));
                        root.IsLegal = false;
                        break;
                    }
                    stack.Peek().Tail = i;
                    stack.Pop();
                }
            }
            if (stack.Count > 1) {
                stack
                    .Where(b => b.Parent != null)
                    .ToList()
                    .ForEach(
                        b => {
                            root
                                .Warnings
                                .AddRange(Complain(text, b.Head, b.Head, Offset).Prepend("Extra '{' found as following:"));
                            root.IsLegal = false;
                        });
            }
            if (root.IsAllLegal) {
                SyntaxCheck(root);
            } 
            return root;
        }

        private static IEnumerable<string> Complain(string text, int head, int tail, int offset) {
            var positions = Range(head, tail - head + 1).ToList();
            var extendeds = Range(1, offset)
                .Aggregate(
                    Empty<int>(),
                    (es, i) => es.Append(head - i).Append(tail + i)
                )
                .Where(p => p >= 0 && p < text.Length);
            var combined = positions.Concat(extendeds).ToList();
            combined.Sort();
            var conext = text
                .Substring(combined.Min(), combined.Count)
                .Replace('\n', '-')
                .Replace('\t', ' ');
            var arrows = combined
                .Select(c => positions.Contains(c) ? '^' : ' ')
                .Join();
            return new[] { conext, arrows };
        }

        public IEnumerable<string> AsComplain() => Complain(Text, Head, Tail, Offset);

        public static void SyntaxCheck(Tree tree) {
            if (!tree.AnyChild)
                return;
            var stack = new Stack<Tree>();
            foreach (var child in tree.Children) {
                SyntaxCheck(child);
                if (child.IsAsk) {
                    tree
                        .Warnings
                        .AddRange(child.AsComplain().Prepend("'ASK' function is found as following"));
                }
                if (child.IsInput) {
                    tree
                        .Warnings
                        .AddRange(child.AsComplain().Prepend("'INPUT' function is found as following"));
                }
                if (child.IsIf) {
                    stack.Push(child);
                } else if (child.IsEndIf) {
                    if (stack.Count < 1) {
                        tree
                            .Warnings
                            .AddRange(child.AsComplain().Prepend("Extra 'End If' command is found as following"));
                        continue;
                    }
                    stack.Pop();
                }
            }
            if (stack.Count > 0) {
                tree
                    .Warnings
                    .AddRange(stack.SelectMany(t => t.AsComplain()).Prepend("Extra 'If' command is found as following"));
            }
        }

        public string Text { get; private set; }

        public IList<Tree> Children { get; }
            = new List<Tree>();

        public Tree Parent { get; set; }

        public bool AnyChild => Children.Count > 0;

        public int Head { get; private set; }
            = -1;

        public int Tail { get; private set; }
            = -1;

        public bool IsLegal { get; private set; }
            = true;

        public bool IsAllLegal
            => this.All(t => t.IsLegal);

        public List<string> Warnings { get; }
            = new List<string>();

        public override string ToString()
            => Text.Substring(Head, Tail - Head + 1);

        /// <summary>
        /// Implement IEnumerable<T> by BFS.
        /// </summary>
        /// <returns></returns>
        public IEnumerator<Tree> GetEnumerator() {
            var queue = new Queue<Tree>();
            queue.Enqueue(this);
            while (queue.Count != 0) {
                var current = queue.Dequeue();
                if (current.AnyChild)
                    current
                        .Children
                        .Aggregate(queue, (q, c) => { q.Enqueue(c); return q; });
                yield return current;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() {
            return GetEnumerator();
        }
    }
}

public static class Program {

    private static void Main(string[] args) {

        Tree.Offset = 35;

        var trees = Directory
             .GetFiles(@"C:\Personal\Projects\documents\DocuDraftFromLynne")
             //.GetFiles(@"C:\CMS.net\DDTMPLT")
             .Where(f => Regex.IsMatch(f, @"(txt|tmp|rtf)$", RegexOptions.IgnoreCase))
             .Select(f => new { FileName = f, Text = File.ReadAllText(f) })
             .Select(f => new { f.FileName, Text = RtfToString.Convert(f.Text) })
             .Select(f => new { f.FileName, Tree = Tree.Create(f.Text) })
             .ToList();
        //trees
        //    .ForEach(f => {
        //        if (f.Tree.IsAllLegal)
        //            Tree.SyntaxCheck(f.Tree);
        //    });
        trees
            .ForEach(f => {
                Console.WriteLine($"----file={f.FileName}\t");
                f
                    .Tree
                    .SelectMany(t => t.Warnings)
                    .ToList()
                    .ForEach(Console.WriteLine);
            });
        //.SelectMany(f /*tree => tree.SelectMany(t => t.Warnings.Append())*/)
        //.ToList()
        //.ForEach(Console.WriteLine);

        //var file = File.ReadAllText(@"C:\Personal\Projects\documents\DocuDraftFromLynne\testForSyntax.rtf");

        //var tree = Tree.Create(/*"{{IF foo}} {{{End IF}}} { if } {ask(dd)} {{input }}"*/ RtfToString.Convert(file));
        //Tree.Offset = 15;
        //Tree.Check(tree);
        //tree
        //    .SelectMany(t => t.Warnings)
        //    .ToList()
        //    .ForEach(Console.WriteLine);
    }
}