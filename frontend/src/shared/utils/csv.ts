export type CsvCell = string | number | boolean | null | undefined

/**
 * Serialises rows to RFC 4180 CSV. Cells that start with =, +, -, @, tab or CR are prefixed with an apostrophe so a
 * spreadsheet never interprets exported user-supplied text as a formula (CSV/formula injection).
 */
export function toCsv(headers: readonly string[], rows: readonly (readonly CsvCell[])[]): string {
  const lines = [headers, ...rows].map((row) => row.map(escapeCell).join(','))
  return lines.join('\r\n')
}

function escapeCell(cell: CsvCell): string {
  if (cell === null || cell === undefined) {
    return ''
  }
  let text = String(cell)
  if (/^[=+\-@\t\r]/.test(text)) {
    text = `'${text}`
  }
  return /[",\r\n]/.test(text) ? `"${text.replaceAll('"', '""')}"` : text
}

export function downloadCsv(filename: string, csv: string): void {
  const blob = new Blob([`﻿${csv}`], { type: 'text/csv;charset=utf-8' }) // BOM so Excel detects UTF-8
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
