import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Modal } from '@/components/ui/dialog'
import { Select } from '@/components/ui/select'
import { toast } from '@/hooks/use-toast'
import { useCopyGenMutation, type ProductCopy } from '../hooks/useCopyGenMutation'

interface SuggestDescriptionButtonProps {
  productId: string
  /** Called when the admin applies the generated copy; the form fills the relevant fields. */
  onApply: (copy: ProductCopy) => void
}

/**
 * Admin "Suggest with AI" affordance (Phase 4, Story 4.2): opens a modal to pick tone + length,
 * generates copy via the LLM, previews it, and applies the chosen copy into the product form.
 * Nothing is saved until the admin saves the product — the AI never writes directly.
 */
export function SuggestDescriptionButton({ productId, onApply }: SuggestDescriptionButtonProps) {
  const [open, setOpen] = useState(false)
  const [tone, setTone] = useState('professional')
  const [length, setLength] = useState('medium')
  const [result, setResult] = useState<ProductCopy | null>(null)
  const copyGen = useCopyGenMutation(productId)

  function close(next: boolean) {
    setOpen(next)
    if (!next) {
      setResult(null)
    }
  }

  function generate() {
    copyGen.mutate(
      { tone, length },
      {
        onSuccess: setResult,
        onError: (error) =>
          toast({
            variant: 'destructive',
            title: 'Couldn’t generate copy',
            description: error instanceof Error ? error.message : 'Please try again.',
          }),
      },
    )
  }

  function apply() {
    if (!result) {
      return
    }
    onApply(result)
    toast({ title: 'Copy applied', description: 'Review it, then save the product to keep it.' })
    close(false)
  }

  return (
    <>
      <Button type="button" variant="outline" size="sm" onClick={() => setOpen(true)}>
        ✨ Suggest with AI
      </Button>

      <Modal
        open={open}
        onOpenChange={close}
        title="Generate product copy"
        description="AI drafts a description, SEO fields, and bullet points. Nothing is saved until you apply and save the product."
        className="max-w-lg"
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1">
              <span className="text-sm font-medium">Tone</span>
              <Select
                aria-label="Tone"
                value={tone}
                onChange={(event) => setTone(event.target.value)}
              >
                <option value="professional">Professional</option>
                <option value="playful">Playful</option>
                <option value="luxury">Luxury</option>
              </Select>
            </div>
            <div className="space-y-1">
              <span className="text-sm font-medium">Length</span>
              <Select
                aria-label="Length"
                value={length}
                onChange={(event) => setLength(event.target.value)}
              >
                <option value="short">Short</option>
                <option value="medium">Medium</option>
                <option value="long">Long</option>
              </Select>
            </div>
          </div>

          {result ? (
            <div className="space-y-3 rounded-md border bg-muted/30 p-3 text-sm">
              <Field label="Description">{result.description}</Field>
              <Field label="SEO title">{result.seoTitle}</Field>
              <Field label="SEO description">{result.seoMetaDescription}</Field>
              {result.bulletPoints?.length ? (
                <div>
                  <p className="font-medium">Bullet points</p>
                  <ul className="list-disc pl-5 text-muted-foreground">
                    {result.bulletPoints.map((point, index) => (
                      <li key={index}>{point}</li>
                    ))}
                  </ul>
                </div>
              ) : null}
            </div>
          ) : null}

          <div className="flex justify-end gap-2">
            {result ? (
              <>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={generate}
                  disabled={copyGen.isPending}
                >
                  {copyGen.isPending ? 'Regenerating…' : 'Regenerate'}
                </Button>
                <Button type="button" size="sm" onClick={apply}>
                  Apply to form
                </Button>
              </>
            ) : (
              <Button type="button" size="sm" onClick={generate} disabled={copyGen.isPending}>
                {copyGen.isPending ? 'Generating…' : 'Generate'}
              </Button>
            )}
          </div>
        </div>
      </Modal>
    </>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="font-medium">{label}</p>
      <p className="whitespace-pre-line text-muted-foreground">{children}</p>
    </div>
  )
}
