import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ChatMessageForm } from './ChatMessageForm'

describe('ChatMessageForm', () => {
  it('sends the typed message', async () => {
    const onSend = vi.fn()
    render(<ChatMessageForm onSend={onSend} disabled={false} />)

    await userEvent.type(screen.getByLabelText('Message'), 'where is my order')
    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(onSend).toHaveBeenCalledWith('where is my order')
  })

  it('does not send an empty message and shows a validation error', async () => {
    const onSend = vi.fn()
    render(<ChatMessageForm onSend={onSend} disabled={false} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send' }))

    expect(onSend).not.toHaveBeenCalled()
    expect(await screen.findByText(/type a message/i)).toBeInTheDocument()
  })

  it('disables the composer while a send is in flight', () => {
    render(<ChatMessageForm onSend={vi.fn()} disabled />)

    expect(screen.getByRole('button', { name: 'Sending…' })).toBeDisabled()
    expect(screen.getByLabelText('Message')).toBeDisabled()
  })

  it('sends on Enter', async () => {
    const onSend = vi.fn()
    render(<ChatMessageForm onSend={onSend} disabled={false} />)

    await userEvent.type(screen.getByLabelText('Message'), 'hi there{Enter}')

    expect(onSend).toHaveBeenCalledWith('hi there')
  })

  it('does not send on Shift+Enter (newline instead)', async () => {
    const onSend = vi.fn()
    render(<ChatMessageForm onSend={onSend} disabled={false} />)

    await userEvent.type(
      screen.getByLabelText('Message'),
      'line one{Shift>}{Enter}{/Shift}line two',
    )

    expect(onSend).not.toHaveBeenCalled()
  })
})
