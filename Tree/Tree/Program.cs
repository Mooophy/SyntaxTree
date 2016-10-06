using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Syntax;
using static System.Linq.Enumerable;

namespace Syntax
{

    #region Helpers
    public static class RtfToString
    {
        /// <summary>
        /// Convert
        /// </summary>
        /// <param name="richText"></param>
        /// <returns></returns>
        public static string Convert(string richText)
        {
            using (var box = new RichTextBox())
            {
                try
                {
                    box.Rtf = richText;
                }
                catch (Exception e)
                {
                    if (e is ArgumentException) return richText;
                    throw;
                }
                return box.Text;
            }
        }
    }

    public static class EnumerableExtensions
    {
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

    public enum SyntaxType
    {
        If,
        EndIf,
        Root,
        Other
    }

    public class Tree
    {
        public static Tree Create(string text)
        {
            var stack = new Stack<Tree>();
            var root = new Tree { Parent = null, Head = -1, Tail = text.Length, Text = text };
            stack.Push(root);
            for (var i = 0; i != text.Length; ++i)
            {
                if (text[i] == '{')
                {
                    var tree = new Tree { Parent = stack.Peek(), Head = i, Text = text };
                    stack.Peek().Children.Add(tree);
                    stack.Push(tree);
                }
                if (text[i] == '}')
                {
                    if (stack.Count < 2)
                    {
                        root.Warnings = root.Warnings.Concat(Complain(text, i, i, 15).Prepend("Extra '}' found as following:"));
                        continue;
                    }
                    stack.Peek().Tail = i;
                    stack.Pop();
                }
            }
            if (stack.Count > 1)
            {
                stack
                    .Where(b => b.Parent != null)
                    .ToList()
                    .ForEach(b => root.Warnings = root.Warnings.Concat(Complain(text, b.Head, b.Head, 15).Prepend("Extra '{' found as following:")));
            }
            return root;
        }

        private static IEnumerable<string> Complain(string text, int head, int tail, int offset)
        {
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

        private static IDictionary<SyntaxType, Func<Tree, bool>> RegexDictionary { get; }
            = new Dictionary<SyntaxType, Func<Tree, bool>> {
                { SyntaxType.If, t => Regex.IsMatch(t.ToString(), @"^{\s*?if\s+.*?}", RegexOptions.IgnoreCase) },
                { SyntaxType.EndIf, t => Regex.IsMatch(t.ToString(), @"^{\s*?end\s+if\s*?}", RegexOptions.IgnoreCase) },
                { SyntaxType.Root, t => t.Parent == null }
            };

        public static void CheckIfAndEndIf(Tree tree)
        {
            if (!tree.AnyChild)
                return;
            var stack = new Stack<Tree>();
            foreach (var child in tree.Children)
            {
                CheckIfAndEndIf(child);
                if (child.GetSyntaxType() == SyntaxType.If)
                    stack.Push(child);
                else if (child.GetSyntaxType() == SyntaxType.EndIf)
                {
                    if (stack.Count < 1)
                    {
                        tree.Warnings = tree.Warnings.Concat(Complain(tree.Text, tree.Head, tree.Tail, 5));
                        continue;
                    }
                    stack.Pop();
                }
            }
            if (stack.Count > 0)
            {
                tree.Warnings = tree.Warnings.Concat(stack.SelectMany(t => Complain(t.Text, t.Head, t.Tail, 5)));
            }

        }

        public string Text { get; private set; }

        public IList<Tree> Children { get; }
            = new List<Tree>();

        public Tree Parent { get; set; }

        public bool AnyChild => Children.Count > 0;

        public int Head { get; private set; }

        public int Tail { get; private set; }

        public IEnumerable<string> Warnings { get; private set; }
            = Enumerable.Empty<string>();

        public int GetHeight()
            => AnyChild ? 1 : 1 + Children.Select(c => c.GetHeight()).Max();

        public SyntaxType GetSyntaxType()
            => RegexDictionary
                .Any(p => p.Value(this))
                ? RegexDictionary.FirstOrDefault(p => p.Value(this)).Key
                : SyntaxType.Other;

        public override string ToString()
            => Text.Substring(Head, Tail - Head + 1);
    }
}

public static class Program
{

    private static void Main(string[] args)
    {
        //var trees = Directory
        //     .GetFiles(@"C:\Personal\Projects\documents\DocuDraftFromLynne")
        //     .Where(f => f.EndsWith("rtf"))
        //     .Select(File.ReadAllText)
        //     .Select(RtfToString.Convert)
        //     .Select(Tree.Create)
        //     .ToList();
        //Console.WriteLine($"Based on files from Lynne Count={trees.Count}, Max Height={trees.Select(t => t.GetHeight()).Max()}");

        //var test0 = Tree.Create("{}");
        //Console.WriteLine($"Based on test Height={test0.GetHeight()}");

        var t = Tree.Create("{IF foo} {{End IF}}");
        Tree.CheckIfAndEndIf(t);
        t
            .Warnings
            .ToList()
            .ForEach(Console.WriteLine);
    }
}