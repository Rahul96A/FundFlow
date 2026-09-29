import { expect, type Page } from '@playwright/test'

const MAILPIT = process.env.E2E_MAILPIT_URL ?? 'http://localhost:8025'

export const PASSWORD = 'Correct-Horse-Battery9'

export function unique(prefix: string): string {
  return `${prefix}${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`
}

/** Polls Mailpit until a message to `to` with a subject containing `subjectPart` arrives; returns its plain-text body. */
export async function waitForEmail(to: string, subjectPart: string, timeoutMs = 45_000): Promise<string> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    const search = await fetch(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:${to}`)}`)
    if (search.ok) {
      const { messages = [] } = (await search.json()) as { messages?: { ID: string; Subject: string }[] }
      const match = messages.find((m) => m.Subject.toLowerCase().includes(subjectPart.toLowerCase()))
      if (match) {
        const detail = await fetch(`${MAILPIT}/api/v1/message/${match.ID}`)
        return ((await detail.json()) as { Text: string }).Text
      }
    }
    await new Promise((resolve) => setTimeout(resolve, 500))
  }
  throw new Error(`No email to ${to} with subject containing "${subjectPart}" arrived within ${timeoutMs} ms. Is Mailpit running at ${MAILPIT}?`)
}

/** Extracts the first app link from an email body and rebases it onto the URL the test is driving. */
export function linkFrom(body: string, baseURL: string): string {
  const match = /https?:\/\/[^\s]+\/(verify-email|accept-invitation|reset-password)\?token=[^\s]+/.exec(body)
  if (!match) {
    throw new Error(`No app link found in email:\n${body}`)
  }
  const url = new URL(match[0])
  return new URL(`${url.pathname}${url.search}`, baseURL).toString()
}

export interface Owner {
  email: string
  organizationName: string
}

/** Registers a brand-new organization through the UI, verifies the email through Mailpit, and signs in. */
export async function registerAndSignIn(page: Page, baseURL: string): Promise<Owner> {
  const suffix = unique('e2e')
  const owner: Owner = { email: `owner-${suffix}@example.test`, organizationName: `E2E Org ${suffix}` }

  await page.goto('/register')
  await page.getByLabel('Organization name').fill(owner.organizationName)
  await page.getByLabel('First name').fill('Olivia')
  await page.getByLabel('Last name').fill('Owner')
  await page.getByLabel('Work email').fill(owner.email)
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD)
  await page.getByLabel('Repeat password').fill(PASSWORD)
  await page.getByRole('button', { name: 'Create organization' }).click()
  await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

  const body = await waitForEmail(owner.email, 'Verify your email')
  await page.goto(linkFrom(body, baseURL))
  await expect(page.getByRole('heading', { name: 'Email verified' })).toBeVisible()

  await page.getByRole('link', { name: 'Continue to sign in' }).click()
  await signIn(page, owner.email)
  await expect(page).toHaveURL(/\/dashboard$/) // wait for the sign-in round trip: navigating away would cancel it
  return owner
}

export async function signIn(page: Page, email: string, password = PASSWORD): Promise<void> {
  await page.getByLabel('Email address').fill(email)
  await page.getByLabel('Password', { exact: true }).fill(password)
  await page.getByRole('button', { name: 'Sign in', exact: true }).click()
}

export async function signOut(page: Page): Promise<void> {
  await page.getByRole('button', { name: 'Account menu' }).click()
  await page.getByRole('menuitem', { name: 'Sign out' }).click()
  await expect(page).toHaveURL(/\/login/)
}

/** Invites a colleague from the Users page. Leaves the invited user's email in the returned value. */
export async function inviteUser(page: Page, opts: { first: string; last: string; role: RegExp }): Promise<string> {
  const email = `${opts.first.toLowerCase()}-${unique('')}@example.test`
  await page.getByRole('link', { name: 'Users' }).click()
  await page.getByRole('button', { name: 'Invite user' }).click()
  const drawer = page.getByRole('presentation').filter({ has: page.getByRole('heading', { name: 'Invite a user' }) })
  await drawer.getByLabel('Email address').fill(email)
  await drawer.getByLabel('First name').fill(opts.first)
  await drawer.getByLabel('Last name').fill(opts.last)
  await drawer.getByLabel('Roles').click()
  await page.getByRole('option', { name: opts.role }).click()
  await drawer.getByRole('button', { name: 'Send invitation' }).click()
  await expect(page.getByText(`Invitation sent to ${email}`)).toBeVisible()
  return email
}

/** Follows the invitation email, sets a password, and lands on the sign-in page. */
export async function acceptInvitation(page: Page, baseURL: string, email: string): Promise<void> {
  const invite = await waitForEmail(email, 'invited')
  await page.goto(linkFrom(invite, baseURL))
  await page.getByLabel('Password', { exact: true }).fill(PASSWORD)
  await page.getByLabel('Repeat password').fill(PASSWORD)
  await page.getByRole('button', { name: 'Activate account' }).click()
  await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
}
