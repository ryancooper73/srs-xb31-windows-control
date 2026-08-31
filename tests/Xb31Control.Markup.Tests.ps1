$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appXamlPath = Join-Path $repositoryRoot 'src\Xb31.Control\App.xaml'
$projectPath = Join-Path $repositoryRoot 'src\Xb31.Control\Xb31.Control.csproj'
$xamlPath = Join-Path $repositoryRoot 'src\Xb31.Control\MainWindow.xaml'
foreach ($requiredPath in @($appXamlPath, $projectPath, $xamlPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "FAIL: XB31 control contract file is missing: $requiredPath"
    }
}

[xml]$appDocument = Get-Content -LiteralPath $appXamlPath -Raw
if ($appDocument.DocumentElement.GetAttribute('ShutdownMode') -ne 'OnExplicitShutdown') {
    throw 'FAIL: XB31 control must use ShutdownMode="OnExplicitShutdown"'
}

[xml]$projectDocument = Get-Content -LiteralPath $projectPath -Raw
$useWindowsForms = $projectDocument.SelectNodes('/Project/PropertyGroup/UseWindowsForms') |
    Select-Object -First 1
if ($null -eq $useWindowsForms -or $useWindowsForms.InnerText -ne 'true') {
    throw 'FAIL: XB31 control project must set UseWindowsForms=true'
}

if (-not (Test-Path -LiteralPath $xamlPath -PathType Leaf)) {
    throw "FAIL: XB31 control window is missing: $xamlPath"
}

$text = Get-Content -LiteralPath $xamlPath -Raw
foreach ($required in @('Select lighting mode', 'Power Off')) {
    if ($text -notmatch [Regex]::Escape($required)) { throw "FAIL: UI omitted '$required'" }
}

foreach ($excluded in @('pending', ('Party ' + 'Booster'))) {
    if ($text -match [Regex]::Escape($excluded)) { throw "FAIL: UI must not include '$excluded'" }
}

[xml]$document = $text

if ($document.DocumentElement.GetAttribute('Title') -ne 'SRS-XB31 Control') {
    throw 'FAIL: window title must use the model name without manufacturer branding'
}

$deviceHeader = $document.SelectNodes('//*') | Where-Object {
    $_.LocalName -eq 'TextBlock' -and $_.GetAttribute('Text') -eq 'SRS-XB31'
} | Select-Object -First 1
if ($null -eq $deviceHeader) {
    throw 'FAIL: device header must use the model name without manufacturer branding'
}

function Get-NamedElement([string]$name) {
    $document.SelectNodes('//*') | Where-Object {
        $_.Attributes | Where-Object { $_.LocalName -eq 'Name' -and $_.Value -eq $name }
    } | Select-Object -First 1
}

function Assert-Attribute($element, [string]$attribute, [string]$expected, [string]$description) {
    if ($null -eq $element -or $element.GetAttribute($attribute) -ne $expected) {
        throw "FAIL: $description must use $attribute=`"$expected`""
    }
}

$window = $document.DocumentElement
if ([int]$window.GetAttribute('Width') -gt 480 -or [int]$window.GetAttribute('Height') -gt 680) {
    throw 'FAIL: XB31 control window must be at most 480x680'
}

$windowStyle = $window.ChildNodes | Where-Object {
    $_.LocalName -eq 'Window.Style'
} | ForEach-Object {
    $_.ChildNodes | Where-Object { $_.LocalName -eq 'Style' }
} | Select-Object -First 1
if ($null -eq $windowStyle) {
    throw 'FAIL: MainWindow must define a Window.Style for visual state changes'
}

$busyTrigger = $windowStyle.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'DataTrigger' -and
    $_.GetAttribute('Binding') -eq '{Binding IsBusy}' -and
    $_.GetAttribute('Value') -eq 'True'
} | Select-Object -First 1
if ($null -eq $busyTrigger) {
    throw 'FAIL: MainWindow style must react to IsBusy=True'
}

foreach ($busySetter in @(
    @{ Property = 'Cursor'; Value = 'Wait' },
    @{ Property = 'ForceCursor'; Value = 'True' }
)) {
    $setter = $busyTrigger.ChildNodes | Where-Object {
        $_.LocalName -eq 'Setter' -and
        $_.GetAttribute('Property') -eq $busySetter.Property -and
        $_.GetAttribute('Value') -eq $busySetter.Value
    } | Select-Object -First 1
    if ($null -eq $setter) {
        throw "FAIL: busy MainWindow must set $($busySetter.Property)=$($busySetter.Value)"
    }
}

$lightingSelector = Get-NamedElement 'LightingSelector'
if ($null -eq $lightingSelector) {
    throw 'FAIL: lighting selector is missing'
}

foreach ($selectionAttribute in @('SelectedIndex', 'SelectedValue')) {
    if ($lightingSelector.HasAttribute($selectionAttribute)) {
        throw 'FAIL: lighting must not have an initial selected mode'
    }
}

Assert-Attribute $lightingSelector 'SelectedItem' '{Binding SelectedLighting, Mode=TwoWay}' 'lighting selector'
Assert-Attribute $lightingSelector 'SelectionChanged' 'LightingSelectionChanged' 'lighting selector'
Assert-Attribute $lightingSelector 'IsEnabled' '{Binding CanInteract}' 'lighting selector'

$selectorContracts = @(
    @{
        Name = 'SoundSelector'
        ItemsSource = '{Binding SoundOptions}'
        SelectedValue = '{Binding SelectedSoundMode, Mode=TwoWay}'
        SelectedValuePath = 'Mode'
        SelectionChanged = 'SoundSelectionChanged'
    },
    @{
        Name = 'AutoStandbySelector'
        ItemsSource = '{Binding AutoStandbyOptions}'
        SelectedValue = '{Binding SelectedAutoStandby, Mode=TwoWay}'
        SelectedValuePath = 'IsOn'
        SelectionChanged = 'AutoStandbySelectionChanged'
    }
)

foreach ($contract in $selectorContracts) {
    $selector = Get-NamedElement $contract.Name
    if ($null -eq $selector) {
        throw "FAIL: selector '$($contract.Name)' is missing"
    }

    foreach ($attribute in @('ItemsSource', 'SelectedValue', 'SelectedValuePath', 'SelectionChanged')) {
        Assert-Attribute $selector $attribute $contract[$attribute] "selector '$($contract.Name)'"
    }

    Assert-Attribute $selector 'DisplayMemberPath' 'Name' "selector '$($contract.Name)'"
    Assert-Attribute $selector 'IsEnabled' '{Binding CanInteract}' "selector '$($contract.Name)'"
}

$sync = Get-NamedElement 'SyncLightingCheckBox'
Assert-Attribute $sync 'Content' 'Sync lighting with display' 'sync setting'
Assert-Attribute $sync 'Checked' 'SyncLightingChecked' 'sync setting'
Assert-Attribute $sync 'Unchecked' 'SyncLightingUnchecked' 'sync setting'
Assert-Attribute $sync 'Foreground' '{StaticResource TextBrush}' 'sync setting'

$startup = Get-NamedElement 'StartWithWindowsCheckBox'
Assert-Attribute $startup 'Content' 'Start XB31 Control with Windows' 'startup setting'
Assert-Attribute $startup 'Foreground' '{StaticResource TextBrush}' 'startup setting'

$closeButton = Get-NamedElement 'CloseWindowButton'
Assert-Attribute $closeButton 'Click' 'CloseClicked' 'close button'

if ($null -ne (Get-NamedElement 'DisplayDiagnosticsText')) {
    throw 'FAIL: temporary display diagnostics must not remain in the main window'
}

$batteryText = $document.SelectNodes('//*') | Where-Object {
    $_.LocalName -eq 'TextBlock' -and $_.GetAttribute('Text') -eq '{Binding BatteryText}'
} | Select-Object -First 1
if ($null -eq $batteryText) {
    throw 'FAIL: battery display must remain bound to BatteryText'
}

$themePath = Join-Path $repositoryRoot 'src\Xb31.Control\Themes\Controls.xaml'
if (-not (Test-Path -LiteralPath $themePath -PathType Leaf)) {
    throw "FAIL: XB31 control theme is missing: $themePath"
}

[xml]$themeDocument = Get-Content -LiteralPath $themePath -Raw
$comboBoxStyle = $themeDocument.SelectNodes('//*') | Where-Object {
    $_.LocalName -eq 'Style' -and $_.GetAttribute('TargetType') -eq 'ComboBox'
} | Select-Object -First 1

if ($null -eq $comboBoxStyle) {
    throw 'FAIL: ComboBox theme style is missing'
}

$comboBackground = $comboBoxStyle.ChildNodes | Where-Object {
    $_.LocalName -eq 'Setter' -and $_.GetAttribute('Property') -eq 'Background'
} | Select-Object -First 1
if ($null -eq $comboBackground -or $comboBackground.GetAttribute('Value') -ne '{StaticResource SurfaceBrush}') {
    throw 'FAIL: closed ComboBox must use the dark SurfaceBrush background'
}

$comboTemplate = $comboBoxStyle.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'ControlTemplate' -and $_.GetAttribute('TargetType') -eq 'ComboBox'
} | Select-Object -First 1

$dropDownToggle = $comboTemplate.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'ToggleButton' -and ($_.Attributes | Where-Object {
        $_.LocalName -eq 'Name' -and $_.Value -eq 'DropDownToggle'
    })
} | Select-Object -First 1
if ($null -eq $dropDownToggle -or $dropDownToggle.GetAttribute('Foreground') -ne '{TemplateBinding Foreground}') {
    throw 'FAIL: closed ComboBox toggle must inherit the template foreground'
}
if ($dropDownToggle.GetAttribute('HorizontalContentAlignment') -ne 'Stretch') {
    throw 'FAIL: closed ComboBox toggle content must stretch to the selector width'
}

$closedPresenter = $comboTemplate.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'ContentPresenter' -and $_.GetAttribute('Content') -eq '{TemplateBinding SelectionBoxItem}'
} | Select-Object -First 1
if ($null -eq $closedPresenter -or $closedPresenter.GetAttribute('TextElement.Foreground') -ne '{TemplateBinding Foreground}') {
    throw 'FAIL: closed ComboBox presenter must apply the template foreground to text content'
}

if ($closedPresenter.GetAttribute('ContentTemplate') -ne '{TemplateBinding SelectionBoxItemTemplate}') {
    throw 'FAIL: closed ComboBox presenter must propagate SelectionBoxItemTemplate'
}

if ($closedPresenter.GetAttribute('ContentTemplateSelector') -ne '{TemplateBinding ItemTemplateSelector}') {
    throw 'FAIL: closed ComboBox presenter must propagate ItemTemplateSelector'
}

$popupBackground = $comboTemplate.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'Border' -and $_.GetAttribute('Background') -eq '#FF111626'
} | Select-Object -First 1
if ($null -eq $popupBackground) {
    throw 'FAIL: ComboBox dropdown must retain its dark background'
}

$disabledComboOpacity = $comboTemplate.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'Trigger' -and $_.GetAttribute('Property') -eq 'IsEnabled' -and $_.GetAttribute('Value') -eq 'False'
} | ForEach-Object {
    $_.ChildNodes | Where-Object {
        $_.LocalName -eq 'Setter' -and $_.GetAttribute('TargetName') -eq 'ComboBorder' -and
        $_.GetAttribute('Property') -eq 'Opacity'
    }
} | Select-Object -First 1
if ($null -eq $disabledComboOpacity -or $disabledComboOpacity.GetAttribute('Value') -ne '0.72') {
    throw 'FAIL: disabled closed ComboBox must retain readable opacity 0.72'
}

$comboBoxItemStyle = $themeDocument.SelectNodes('//*') | Where-Object {
    $_.LocalName -eq 'Style' -and $_.GetAttribute('TargetType') -eq 'ComboBoxItem'
} | Select-Object -First 1

if ($null -eq $comboBoxItemStyle) {
    throw 'FAIL: ComboBoxItem theme style is missing'
}

$itemForeground = $comboBoxItemStyle.ChildNodes | Where-Object {
    $_.LocalName -eq 'Setter' -and $_.GetAttribute('Property') -eq 'Foreground'
} | Select-Object -First 1
if ($null -eq $itemForeground -or $itemForeground.GetAttribute('Value') -ne '{StaticResource TextBrush}') {
    throw 'FAIL: enabled ComboBox items must use TextBrush'
}

$disabledItemForeground = $comboBoxItemStyle.SelectNodes('.//*') | Where-Object {
    $_.LocalName -eq 'Trigger' -and $_.GetAttribute('Property') -eq 'IsEnabled' -and $_.GetAttribute('Value') -eq 'False'
} | ForEach-Object {
    $_.ChildNodes | Where-Object {
        $_.LocalName -eq 'Setter' -and $_.GetAttribute('Property') -eq 'Foreground'
    }
} | Select-Object -First 1
if ($null -eq $disabledItemForeground -or $disabledItemForeground.GetAttribute('Value') -ne '{StaticResource MutedBrush}') {
    throw 'FAIL: disabled ComboBox items must use MutedBrush'
}

Write-Host 'PASS: XB31 control markup contract'
