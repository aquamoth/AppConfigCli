namespace AppConfigCli;

// Navigates command history with a persistent bottom draft, independent of console I/O.
internal sealed class HistoryNavigator
{
    private readonly IList<string> _history; // oldest..newest
    private int _index; // 0.._history.Count; Count means bottom slot
    private string _draft = string.Empty;
    private bool _modifiedFromHistory = false;

    public HistoryNavigator(IList<string> history)
    {
        _history = history;
        _index = _history.Count;
        Text = string.Empty;
    }

    public int Index => _index;
    public string Text { get; private set; }

    public void Up()
    {
        if (_index <= 0) return;
        if (_index == _history.Count)
        {
            _draft = Text;
        }
        _index--;
        Text = _history[_index];
        _modifiedFromHistory = false;
    }

    public void Down()
    {
        if (_index >= _history.Count) return;
        _index++;
        if (_index == _history.Count)
        {
            Text = _draft;
        }
        else
        {
            Text = _history[_index];
        }
        _modifiedFromHistory = false;
    }

    public void TypeChar(char ch)
    {
        TransitionToDraftIfEditingHistory();
        Text += ch;
        _draft = Text;
    }

    public void Backspace()
    {
        TransitionToDraftIfEditingHistory();
        if (Text.Length == 0) return;
        Text = Text.Substring(0, Text.Length - 1);
        _draft = Text;
    }

    private void TransitionToDraftIfEditingHistory()
    {
        if (_index != _history.Count && !_modifiedFromHistory)
        {
            _modifiedFromHistory = true;
            _draft = Text;
            _index = _history.Count;
            Text = _draft;
        }
    }
}

