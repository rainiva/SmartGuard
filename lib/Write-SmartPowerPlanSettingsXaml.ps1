#Requires -Version 5.1
param([string]$ScriptRoot = 'C:\Tools')

$xamlPath = Join-Path $ScriptRoot 'lib\SmartPowerPlan.Settings.xaml'
$dir = Split-Path -Parent $xamlPath
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="智能电源计划设置"
        Height="560" Width="460"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        Background="#F3F3F3"
        FontFamily="Microsoft YaHei UI"
        FontSize="12"
        TextOptions.TextFormattingMode="Display"
        UseLayoutRounding="True"
        SnapsToDevicePixels="True">
  <Window.Resources>
    <Style x:Key="SectionTitle" TargetType="TextBlock">
      <Setter Property="FontSize" Value="13"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Foreground" Value="#202020"/>
      <Setter Property="Margin" Value="0,0,0,12"/>
    </Style>
    <Style x:Key="FieldLabel" TargetType="TextBlock">
      <Setter Property="Foreground" Value="#323232"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style x:Key="ValueBadge" TargetType="TextBlock">
      <Setter Property="Foreground" Value="#0078D4"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="HorizontalAlignment" Value="Right"/>
      <Setter Property="MinWidth" Value="88"/>
      <Setter Property="TextAlignment" Value="Right"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style x:Key="CardBorder" TargetType="Border">
      <Setter Property="Background" Value="White"/>
      <Setter Property="CornerRadius" Value="10"/>
      <Setter Property="Padding" Value="18,16"/>
      <Setter Property="Margin" Value="0,0,0,14"/>
      <Setter Property="Effect">
        <Setter.Value>
          <DropShadowEffect Color="#1A000000" BlurRadius="14" ShadowDepth="2" Opacity="0.35"/>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="PrimaryButton" TargetType="Button">
      <Setter Property="Background" Value="#0078D4"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
      <Setter Property="Padding" Value="20,9"/>
      <Setter Property="MinWidth" Value="96"/>
      <Setter Property="BorderThickness" Value="0"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="bd" Background="{TemplateBinding Background}" CornerRadius="6" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="bd" Property="Background" Value="#106EBE"/>
              </Trigger>
              <Trigger Property="IsPressed" Value="True">
                <Setter TargetName="bd" Property="Background" Value="#005A9E"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style x:Key="SecondaryButton" TargetType="Button">
      <Setter Property="Background" Value="White"/>
      <Setter Property="Foreground" Value="#323232"/>
      <Setter Property="Padding" Value="20,9"/>
      <Setter Property="MinWidth" Value="96"/>
      <Setter Property="BorderBrush" Value="#D6D6D6"/>
      <Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template">
        <Setter.Value>
          <ControlTemplate TargetType="Button">
            <Border x:Name="bd" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="6" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True">
                <Setter TargetName="bd" Property="Background" Value="#F8F8F8"/>
              </Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
        </Setter.Value>
      </Setter>
    </Style>
    <Style TargetType="Slider">
      <Setter Property="Margin" Value="0,6,0,0"/>
      <Setter Property="VerticalAlignment" Value="Center"/>
    </Style>
    <Style TargetType="CheckBox">
      <Setter Property="Margin" Value="0,0,0,10"/>
      <Setter Property="Foreground" Value="#323232"/>
    </Style>
  </Window.Resources>
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="84"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="64"/>
    </Grid.RowDefinitions>
    <Border Grid.Row="0" Background="#0078D4">
      <StackPanel Margin="24,18,24,0" VerticalAlignment="Center">
        <TextBlock Text="智能电源计划" FontSize="17" FontWeight="SemiBold" Foreground="White"/>
        <TextBlock Text="电源计划守护设置" FontSize="12" Foreground="#DCEBFF" Margin="0,4,0,0"/>
      </StackPanel>
    </Border>
    <ScrollViewer Grid.Row="1" VerticalScrollBarVisibility="Auto" Padding="22,18,22,6">
      <StackPanel>
        <Border Style="{StaticResource CardBorder}">
          <StackPanel>
            <TextBlock Text="空闲与电池" Style="{StaticResource SectionTitle}"/>
            <Grid Margin="0,0,0,10">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="88"/>
              </Grid.ColumnDefinitions>
              <TextBlock Text="空闲后切换平衡（分钟）" Style="{StaticResource FieldLabel}"/>
              <TextBlock x:Name="lblBalanced" Grid.Column="1" Style="{StaticResource ValueBadge}"/>
            </Grid>
            <Slider x:Name="sldBalanced" Minimum="1" Maximum="120" TickFrequency="1" IsSnapToTickEnabled="True"/>
            <Grid Margin="0,8,0,10">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="88"/>
              </Grid.ColumnDefinitions>
              <TextBlock Text="空闲后切换节能（分钟）" Style="{StaticResource FieldLabel}"/>
              <TextBlock x:Name="lblSaver" Grid.Column="1" Style="{StaticResource ValueBadge}"/>
            </Grid>
            <Slider x:Name="sldSaver" Minimum="2" Maximum="240" TickFrequency="1" IsSnapToTickEnabled="True"/>
            <Grid Margin="0,8,0,10">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="88"/>
              </Grid.ColumnDefinitions>
              <TextBlock Text="低电量阈值（%）" Style="{StaticResource FieldLabel}"/>
              <TextBlock x:Name="lblBattery" Grid.Column="1" Style="{StaticResource ValueBadge}"/>
            </Grid>
            <Slider x:Name="sldBattery" Minimum="0" Maximum="100" TickFrequency="5" IsSnapToTickEnabled="True"/>
          </StackPanel>
        </Border>
        <Border Style="{StaticResource CardBorder}">
          <StackPanel>
            <TextBlock Text="运行参数" Style="{StaticResource SectionTitle}"/>
            <Grid Margin="0,0,0,10">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="88"/>
              </Grid.ColumnDefinitions>
              <TextBlock Text="轮询间隔（秒）" Style="{StaticResource FieldLabel}"/>
              <TextBlock x:Name="lblPoll" Grid.Column="1" Style="{StaticResource ValueBadge}"/>
            </Grid>
            <Slider x:Name="sldPoll" Minimum="5" Maximum="120" TickFrequency="5" IsSnapToTickEnabled="True"/>
            <Grid Margin="0,8,0,10">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*"/>
                <ColumnDefinition Width="88"/>
              </Grid.ColumnDefinitions>
              <TextBlock Text="亮度恢复延迟（毫秒）" Style="{StaticResource FieldLabel}"/>
              <TextBlock x:Name="lblBrightMs" Grid.Column="1" Style="{StaticResource ValueBadge}"/>
            </Grid>
            <Slider x:Name="sldBrightMs" Minimum="0" Maximum="3000" TickFrequency="50" IsSnapToTickEnabled="True"/>
          </StackPanel>
        </Border>
        <Border Style="{StaticResource CardBorder}" Margin="0,0,0,0">
          <StackPanel>
            <TextBlock Text="通知" Style="{StaticResource SectionTitle}"/>
            <CheckBox x:Name="chkPaused" Content="暂停守护（仅监控，不切换计划）"/>
            <CheckBox x:Name="chkNotify" Content="计划切换时显示托盘气泡" Margin="0"/>
          </StackPanel>
        </Border>
      </StackPanel>
    </ScrollViewer>
    <Border Grid.Row="2" Background="#F3F3F3" BorderBrush="#E5E5E5" BorderThickness="0,1,0,0" Padding="22,0">
      <Grid VerticalAlignment="Center">
        <TextBlock Text="保存后立即生效" Foreground="#606060" VerticalAlignment="Center"/>
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
          <Button x:Name="btnCancel" Content="取消" Style="{StaticResource SecondaryButton}" Margin="0,0,10,0"/>
          <Button x:Name="btnSave" Content="保存" Style="{StaticResource PrimaryButton}" IsDefault="True"/>
        </StackPanel>
      </Grid>
    </Border>
  </Grid>
</Window>
'@

$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($xamlPath, $xaml, $utf8)
Write-Host ('已写入: ' + $xamlPath)
