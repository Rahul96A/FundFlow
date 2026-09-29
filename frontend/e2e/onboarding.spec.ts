import { expect, test } from '@playwright/test'
import { acceptInvitation, inviteUser, linkFrom, registerAndSignIn, signIn, signOut, waitForEmail } from './helpers.ts'

test.describe('access control', () => {
  test('anonymous visitors are sent to sign in and come back afterwards', async ({ page }) => {
    await page.goto('/settings/users')

    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
  })

  test('wrong credentials show a clear error and no session', async ({ page }) => {
    await page.goto('/login')
    await signIn(page, 'nobody@example.test', 'Not-The-Right-Password1')

    await expect(page.getByRole('alert')).toContainText('Invalid email or password')
    await expect(page).toHaveURL(/\/login$/)
  })

  test('signed-out visitors cannot tell which URLs exist: unknown pages also lead to sign in', async ({ page }) => {
    await page.goto('/no/such/page')

    await expect(page).toHaveURL(/\/login$/)
    await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
  })
})

test.describe('signed-in navigation', () => {
  test('the session survives a full page reload, and an unknown URL shows a helpful not-found page inside the app', async ({ page, baseURL }) => {
    await registerAndSignIn(page, baseURL!)

    // A full page load restores the session silently from the HttpOnly refresh cookie.
    await page.reload()
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Olivia')

    await page.goto('/no/such/page')
    await expect(page.getByRole('heading', { name: 'Page not found' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
    await page.getByRole('link', { name: /dashboard/i }).first().click()
    await expect(page).toHaveURL(/\/dashboard$/)
  })
})

test.describe('a new organization', () => {
  test('registers, verifies by email, signs in, invites a colleague who joins', async ({ page, baseURL }) => {
    const owner = await registerAndSignIn(page, baseURL!)

    // Dashboard: greeting, team numbers, setup checklist.
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Olivia')
    await expect(page.getByText('Team members')).toBeVisible()
    await expect(page.getByRole('list', { name: 'Setup checklist' })).toBeVisible()
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toContainText('Users')

    // Users: the owner is listed; invite a colleague with a role.
    await page.getByRole('link', { name: 'Users' }).click()
    await expect(page.getByRole('heading', { name: 'Users', exact: true })).toBeVisible()
    await expect(page.getByRole('table', { name: 'Users' })).toContainText(owner.email)

    const colleague = await inviteUser(page, { first: 'Casey', last: 'Colleague', role: /Fundraising manager/ })
    await expect(page.getByRole('table', { name: 'Users' })).toContainText(colleague)

    // The invitee joins from their own email link (after the owner signs out of this browser).
    await signOut(page)
    await acceptInvitation(page, baseURL!, colleague)
    await signIn(page, colleague)
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Casey')

    // Signing out ends the session: protected pages send you back to sign in.
    await signOut(page)
    await page.goto('/dashboard')
    await expect(page).toHaveURL(/\/login$/)
  })

  test('an invited colleague sees only what their role allows', async ({ page, baseURL }) => {
    await registerAndSignIn(page, baseURL!)
    const colleague = await inviteUser(page, { first: 'Sam', last: 'Staff', role: /^Staff/ })
    await signOut(page)
    await acceptInvitation(page, baseURL!, colleague)
    await signIn(page, colleague)

    await expect(page.getByRole('heading', { level: 1 })).toContainText('Sam')
    const nav = page.getByRole('navigation', { name: 'Main navigation' })
    await expect(nav).toContainText('Organization')
    await expect(nav).not.toContainText('Users')
    await expect(nav).not.toContainText('Audit log')

    // Typing the URL does not bypass the UI guard (and the API refuses the data as well).
    await page.goto('/settings/users')
    await expect(page.getByRole('heading', { name: 'You do not have access to this page' })).toBeVisible()
  })

  test('password reset by email works end to end', async ({ page, baseURL }) => {
    const owner = await registerAndSignIn(page, baseURL!)
    await signOut(page)

    await page.getByRole('link', { name: 'Forgot your password?' }).click()
    await page.getByLabel('Email address').fill(owner.email)
    await page.getByRole('button', { name: 'Send reset link' }).click()
    await expect(page.getByRole('heading', { name: 'Check your inbox' })).toBeVisible()

    const mail = await waitForEmail(owner.email, 'Reset your')
    await page.goto(linkFrom(mail, baseURL!))
    const fresh = 'Brand-New-Passw0rd-77'
    await page.getByLabel('New password', { exact: true }).fill(fresh)
    await page.getByLabel('Repeat new password').fill(fresh)
    await page.getByRole('button', { name: 'Update password' }).click()

    await expect(page.getByRole('heading', { name: 'Welcome back' })).toBeVisible()
    await signIn(page, owner.email, fresh)
    await expect(page.getByRole('heading', { level: 1 })).toContainText('Olivia')
  })
})

test.describe('@mobile responsive layout', () => {
  test('the navigation collapses into a drawer on phones', async ({ page, baseURL }) => {
    await registerAndSignIn(page, baseURL!)

    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeHidden()
    await page.getByRole('button', { name: 'Open navigation' }).click()
    await expect(page.getByRole('navigation', { name: 'Main navigation' })).toBeVisible()
    await page.getByRole('link', { name: 'Organization' }).click()
    await expect(page.getByRole('heading', { name: 'Organization', exact: true })).toBeVisible()
  })
})
