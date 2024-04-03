using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Humanizer;

namespace MicroPlumberd.SpecFlow.SourceGenerators
{
    public readonly struct Word : IEquatable<Word>
    {
        public Word Pluralize()
        {
            return new Word(Text.Pluralize());
        }
        public bool Equals(Word other)
        {
            return Text == other.Text;
        }

        public override bool Equals(object obj)
        {
            return obj is Word other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Text != null ? Text.GetHashCode() : 0);
        }

        public static bool operator ==(Word left, Word right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Word left, Word right)
        {
            return !left.Equals(right);
        }

        public string Text { get; }

        public Word(string text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        // Implicit conversion from string to Word
        public static implicit operator Word(string text) => new Word(text);

        // Override ToString() to easily convert Word back to string
        public override string ToString() => Text;

        public Word ToLower()
        {
            return new Word(Text.ToLower());
        }

        public char this[int i] => Text[i];

        public Word ToCapitalizedRegex()
        {
            if (Text.Length < 1 || !Char.IsLetter(this[0])) return this;
            var rest = Text.Length > 1 ? this.Text.Substring(1) : string.Empty;
            
            var c = this.Text[0];
            return $"[{Char.ToLower(c)}|{Char.ToUpper(c)}]{rest}";
        }
    }
    public struct Sentence : IEnumerable<Word>
    {
        private readonly List<Word> _words;

        public Sentence ChangeWord(int index, Func<Word, Word> ch)
        {
            var tmp = _words.ToList();
            tmp[index] = ch(tmp[index]);
            return new Sentence(tmp);
        }

        public Sentence ChangeEachWord(Func<Word, Word> ch)
        {
            var tmp = _words.ToList();
            for (int i = 0; i < tmp.Count; i++)
                tmp[i] = ch(tmp[i]);
            return new Sentence(tmp);
        }

        public Sentence(IEnumerable<string> words) : this(words.Select(x => (Word)x)){}
        public Sentence(IEnumerable<Word> words)
        {
            this._words = new List<Word>(words);
        }

        private Sentence(List<Word> words)
        {
            _words = words;
        }
        public static implicit operator Sentence(string text) => new Sentence(text.Humanize().Split(' '));
        public IReadOnlyList<Word> Words => _words.AsReadOnly();

        // Addition operator to add a word to a sentence
        public static Sentence operator +(Sentence sentence, Word word)
        {
            var newWords = sentence._words.ToList();
            newWords.Add(word);
            return new Sentence(newWords);
        }
        public static Sentence operator +(Sentence sentence, Sentence right)
        {
           return new Sentence(sentence.Union(right));
        }
        public static Sentence operator +(Word word, Sentence sentence)
        {
            var newWords = new List<Word>() { word };
            foreach(var i in sentence) newWords.Add(i);
            return new Sentence(newWords);
        }

        // Subtraction operator to remove a word from a sentence
        public static Sentence operator -(Sentence sentence, Word word)
        {
            var newWords = sentence._words.Where(w => w.Text != word.Text).ToList();
            return new Sentence(newWords);
        }

        public Sentence Remove(Word w)
        {
            return _words.Contains(w) ? new Sentence(_words.Where(x => !x.Equals(w))) : this;
        }
        // Method to remove duplicate words
        public Sentence RemoveDuplicates()
        {
            var uniqueWords = _words.Distinct().ToList();
            return new Sentence(uniqueWords);
        }

        public IEnumerator<Word> GetEnumerator()
        {
            return _words.GetEnumerator();
        }

        public int Count => _words.Count;
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_words).GetEnumerator();
        }

        public Sentence Insert(int index, Word word)
        {
            var tmp = this._words.ToList();
            tmp.Insert(index,word);
            return new Sentence(tmp);
        }

        public Sentence InsertBackwards(int index, Word word)
        {
            var tmp = this._words.ToList();
            tmp.Insert(tmp.Count - index, word);
            return new Sentence(tmp);
        }

        public override string ToString()
        {
            return string.Join(" ", this._words.Select(x => x.Text));
        }

        public string Dehumanize()
        {
            return ToString().Dehumanize();
        }

        public Sentence ToLower()
        {
            return new Sentence(_words.Select(x => x.ToLower()));
        }

        public Sentence ToCapitalizedRegex()
        {
            return new Sentence(_words.Select(x => x.ToCapitalizedRegex()));
        }
    }
}