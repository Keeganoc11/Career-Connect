import { UpgradePanel } from '../components/UpgradePanel'
import { PageHeader } from '../components/ui'

interface Props {
  title: string
  /** One line under the title, in the voice of the page this stands in for. */
  description: string
  panelTitle: string
  panelDescription: string
}

/**
 * A whole page a Free account can't use — Tailor and Resumes. The page keeps
 * its title and place in the tabs so the app doesn't appear to lose features
 * between plans; the panel explains what's behind it.
 */
export function UpgradePage({ title, description, panelTitle, panelDescription }: Props) {
  return (
    <div>
      <PageHeader title={title} description={description} />
      <UpgradePanel title={panelTitle} description={panelDescription} />
    </div>
  )
}
