import { toCsv } from './csv'

describe('toCsv', () => {
  it('writes a header and rows separated by CRLF', () => {
    expect(toCsv(['Name', 'Age'], [['Ada', 36], ['Grace', 85]])).toBe('Name,Age\r\nAda,36\r\nGrace,85')
  })

  it('quotes cells containing commas, quotes or newlines and doubles embedded quotes', () => {
    const csv = toCsv(['Note'], [['a,b'], ['say "hi"'], ['line1\nline2']])
    expect(csv).toBe('Note\r\n"a,b"\r\n"say ""hi"""\r\n"line1\nline2"')
  })

  it('renders null and undefined as empty cells and keeps booleans readable', () => {
    expect(toCsv(['a', 'b', 'c'], [[null, undefined, true]])).toBe('a,b,c\r\n,,true')
  })

  it.each(['=SUM(A1:A9)', '+1+1', '-2+3', '@cmd', '\tinjected'])('neutralises formula injection in %j', (value) => {
    const line = toCsv(['x'], [[value]]).split('\r\n')[1] ?? ''
    // The cell must not begin with a formula trigger once a spreadsheet parses it.
    expect(line.replace(/^"/, '')).toMatch(/^'/)
  })

  it('does not alter ordinary negative-looking text in the middle of a cell', () => {
    expect(toCsv(['x'], [['a-b']])).toBe('x\r\na-b')
  })
})
