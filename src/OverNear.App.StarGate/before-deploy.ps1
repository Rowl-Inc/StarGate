if(Enter-OncePerDeployment "set_env_vars")
{
	#singleton per deployment
	$items = Get-ChildItem Env:
	$reboot = $false
	$iisreset = $false
	foreach ($i in $items)
	{
		if($i.name.StartsWith("ENV.SET.")) 
		{
			$n = $i.name -replace "^ENV\.SET\.", ""
			[Environment]::SetEnvironmentVariable($n, $i.value, "Machine")
			Write-Host "SET" $n $i.value
		}
		elseif($i.name.StartsWith("ENV.RM.")) 
		{
			$n = $i.name -replace "^ENV\.RM\.", ""
			[Environment]::SetEnvironmentVariable($n, $null, "Machine")
			Write-Host "RM" $n $i.value
		}
		elseif($i.name.CompareTo("IIS.RESET") -eq 0)
		{
			if($i.value.ToLower().CompareTo("true") -eq 0) 
			{
				$iisreset = $true;
			}
		}
		elseif($i.name.CompareTo("SYS.REBOOT") -eq 0) 
		{
			if($i.value.ToLower().CompareTo("true") -eq 0) 
			{
				$reboot = $true;
			}
		}
		else {
		}
	}

	if($reboot) 
	{
		Restart-Computer
	}
	elseif($iisreset)
	{
		iisreset
	}
	else 
	{
	}
}
